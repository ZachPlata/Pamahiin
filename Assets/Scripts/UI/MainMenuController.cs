using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour
{
    private UIDocument uiDocument;
    
    private VisualElement mainMenuView;
    private VisualElement multiplayerView;
    private VisualElement lobbyView;
    private VisualElement helpView;
    private VisualElement settingsView;
    
    private TextField ipInput;
    private ScrollView playerListContainer;

    // Settings controls
    private Slider sliderMaster, sliderMusic, sliderSFX, sliderVoice;
    private Label  lblMaster, lblMusic, lblSFX, lblVoice;
    private Toggle toggleFullscreen, toggleFPS;
    private Toggle toggleTutorialHints;

    [Header("Atmospheric VFX")]
    private Label titleLabel;
    private VisualElement vignette;
    private VisualElement bgMist;
    private Texture2D generatedVignetteTex;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        var root = uiDocument.rootVisualElement;

        // Views
        mainMenuView = root.Q<VisualElement>("main-menu-view");
        multiplayerView = root.Q<VisualElement>("multiplayer-view");
        lobbyView = root.Q<VisualElement>("lobby-view");
        helpView = root.Q<VisualElement>("help-view");
        settingsView = root.Q<VisualElement>("settings-view");

        // VFX Elements
        titleLabel = root.Q<Label>(className: "title-label");
        vignette = root.Q<VisualElement>("vignette");
        bgMist = root.Q<VisualElement>("bg-mist");

        if (vignette != null)
        {
            GenerateVignetteTexture();
            vignette.style.backgroundImage = new StyleBackground(generatedVignetteTex);
        }

        // Main Menu Buttons
        root.Q<Button>("btn-singleplayer").clicked += OnSingleplayerClicked;
        root.Q<Button>("btn-multiplayer").clicked += OnMultiplayerClicked;
        root.Q<Button>("btn-settings").clicked += ShowSettingsMenu;
        root.Q<Button>("btn-help").clicked += ShowHelpMenu;
        root.Q<Button>("btn-quit").clicked += OnQuitClicked;

        // Settings View
        root.Q<Button>("btn-back-from-settings").clicked += ShowMainMenu;

        sliderMaster = root.Q<Slider>("slider-master");
        sliderMusic  = root.Q<Slider>("slider-music");
        sliderSFX    = root.Q<Slider>("slider-sfx");
        sliderVoice  = root.Q<Slider>("slider-voice");

        lblMaster = root.Q<Label>("lbl-master");
        lblMusic  = root.Q<Label>("lbl-music");
        lblSFX    = root.Q<Label>("lbl-sfx");
        lblVoice  = root.Q<Label>("lbl-voice");

        toggleFullscreen = root.Q<Toggle>("toggle-fullscreen");
        toggleFPS        = root.Q<Toggle>("toggle-fps");
        toggleTutorialHints = root.Q<Toggle>("toggle-tutorial-hints");

        if (sliderMaster != null)
            sliderMaster.RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetMasterVolume(e.newValue); UpdateVolLabel(lblMaster, e.newValue); });
        if (sliderMusic != null)
            sliderMusic.RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetMusicVolume(e.newValue); UpdateVolLabel(lblMusic, e.newValue); });
        if (sliderSFX != null)
            sliderSFX.RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetSFXVolume(e.newValue); UpdateVolLabel(lblSFX, e.newValue); });
        if (sliderVoice != null)
            sliderVoice.RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetVoiceVolume(e.newValue); UpdateVolLabel(lblVoice, e.newValue); });

        if (toggleFullscreen != null)
            toggleFullscreen.RegisterValueChangedCallback(e => GameSettingsManager.Instance?.SetFullscreen(e.newValue));
        if (toggleFPS != null)
            toggleFPS.RegisterValueChangedCallback(e => GameSettingsManager.Instance?.SetShowFPS(e.newValue));
        if (toggleTutorialHints != null)
            toggleTutorialHints.RegisterValueChangedCallback(e => GameSettingsManager.Instance?.SetTutorialHintsEnabled(e.newValue));

        SyncSettingsFromManager();

        // Multiplayer View
        root.Q<Button>("btn-host").clicked += OnHostClicked;
        root.Q<Button>("btn-join").clicked += OnJoinClicked;
        root.Q<Button>("btn-back-to-main").clicked += ShowMainMenu;
        ipInput = root.Q<TextField>("input-ip");

        // Lobby View
        root.Q<Button>("btn-start-game").clicked += OnStartGameClicked;
        root.Q<Button>("btn-leave-lobby").clicked += OnLeaveLobbyClicked;
        playerListContainer = root.Q<ScrollView>("lobby-player-list");

        // Help View
        root.Q<Button>("btn-back-from-help").clicked += ShowMainMenu;

        // Auto-show lobby if returning from a networked game
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
        {
            ShowLobbyMenu();
        }
        else
        {
            ShowMainMenu();
        }
        
        // Listen to network manager events for joining as client
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStarted += OnClientStarted;
            NetworkManager.Singleton.OnClientStopped += OnClientStopped;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void OnDisable()
    {
        GameSettingsManager.Instance?.SaveAll();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStarted -= OnClientStarted;
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void GenerateVignetteTexture()
    {
        if (generatedVignetteTex == null)
        {
            generatedVignetteTex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            Vector2 center = new Vector2(128, 128);
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center) / 128f;
                    float alpha = Mathf.Clamp01(Mathf.Pow(dist, 2.5f)); 
                    generatedVignetteTex.SetPixel(x, y, new Color(0, 0, 0, alpha * 0.95f));
                }
            }
            generatedVignetteTex.Apply();
        }
    }

    private void Update()
    {
        // Atmospheric VFX Loop
        if (titleLabel != null)
        {
            // Flickering title
            if (Random.value < 0.05f)
            {
                titleLabel.style.opacity = Random.Range(0.4f, 1f);
            }
        }

        if (vignette != null)
        {
            // Flickering vignette
            vignette.style.opacity = 0.8f + Mathf.Sin(Time.time * 8f) * 0.1f + Random.Range(-0.05f, 0.05f);
        }

        if (bgMist != null)
        {
            // Fading mist opacity
            float alpha = 0.3f + Mathf.Sin(Time.time * 0.5f) * 0.1f;
            bgMist.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, alpha));
        }
    }

    private void ShowMainMenu()
    {
        HideAllViews();
        GameSettingsManager.Instance?.SaveAll();
        mainMenuView.RemoveFromClassList("hidden-view");
        mainMenuView.AddToClassList("active-view");
    }

    private void ShowSettingsMenu()
    {
        HideAllViews();
        SyncSettingsFromManager();
        if (settingsView != null)
        {
            settingsView.RemoveFromClassList("hidden-view");
            settingsView.AddToClassList("active-view");
        }
    }

    private void SyncSettingsFromManager()
    {
        var s = GameSettingsManager.Instance;
        if (s == null) return;

        if (sliderMaster != null) sliderMaster.value = s.MasterVolume;
        if (sliderMusic != null)  sliderMusic.value = s.MusicVolume;
        if (sliderSFX != null)    sliderSFX.value = s.SFXVolume;
        if (sliderVoice != null)  sliderVoice.value = s.VoiceVolume;

        if (toggleFullscreen != null) toggleFullscreen.value = s.IsFullscreen;
        if (toggleFPS != null)        toggleFPS.value = s.ShowFPS;
        if (toggleTutorialHints != null)  toggleTutorialHints.value = s.TutorialHintsEnabled;

        UpdateVolLabel(lblMaster, s.MasterVolume);
        UpdateVolLabel(lblMusic,  s.MusicVolume);
        UpdateVolLabel(lblSFX,    s.SFXVolume);
        UpdateVolLabel(lblVoice,  s.VoiceVolume);
    }

    private static void UpdateVolLabel(Label label, float linear)
    {
        if (label != null)
            label.text = $"{Mathf.RoundToInt(linear * 100f)}%";
    }

    private void ShowMultiplayerMenu()
    {
        HideAllViews();
        multiplayerView.RemoveFromClassList("hidden-view");
        multiplayerView.AddToClassList("active-view");
    }

    private void ShowLobbyMenu()
    {
        HideAllViews();
        lobbyView.RemoveFromClassList("hidden-view");
        lobbyView.AddToClassList("active-view");
        
        // Only the host can start the game
        var startBtn = uiDocument.rootVisualElement.Q<Button>("btn-start-game");
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            startBtn.style.display = DisplayStyle.Flex;
        }
        else
        {
            startBtn.style.display = DisplayStyle.None;
        }

        RefreshPlayerList();
    }

    private void ShowHelpMenu()
    {
        HideAllViews();
        helpView.RemoveFromClassList("hidden-view");
        helpView.AddToClassList("active-view");
    }

    private void HideAllViews()
    {
        mainMenuView.RemoveFromClassList("active-view");
        mainMenuView.AddToClassList("hidden-view");
        
        multiplayerView.RemoveFromClassList("active-view");
        multiplayerView.AddToClassList("hidden-view");
        
        lobbyView.RemoveFromClassList("active-view");
        lobbyView.AddToClassList("hidden-view");
        
        helpView.RemoveFromClassList("active-view");
        helpView.AddToClassList("hidden-view");

        if (settingsView != null)
        {
            settingsView.RemoveFromClassList("active-view");
            settingsView.AddToClassList("hidden-view");
        }
    }

    private void OnSingleplayerClicked()
    {
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.StartSingleplayer();
        }
    }

    private void OnMultiplayerClicked()
    {
        ShowMultiplayerMenu();
    }

    private void OnHostClicked()
    {
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.HostGame();
            ShowLobbyMenu();
        }
    }

    private void OnJoinClicked()
    {
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.JoinGame(ipInput.value);
        }
    }

    private void OnClientStarted()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            ShowLobbyMenu();
        }
    }

    private void OnClientStopped(bool wasHost)
    {
        ShowMainMenu();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (lobbyView.ClassListContains("active-view"))
        {
            RefreshPlayerList();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (lobbyView.ClassListContains("active-view"))
        {
            RefreshPlayerList();
        }
    }

    private void RefreshPlayerList()
    {
        if (playerListContainer == null || NetworkManager.Singleton == null) return;
        
        playerListContainer.Clear();

        if (NetworkManager.Singleton.IsServer)
        {
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                AddPlayerSlotToUI(clientId);
            }
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            // Clients can't easily get the full list of ConnectedClientsIds from NGO without a custom NetworkList.
            // For this UI mockup, we'll just show the local player if they are a client.
            // A robust implementation would use a NetworkList<ulong> on the GameMatchManager to sync lobby members.
            AddPlayerSlotToUI(NetworkManager.Singleton.LocalClientId);
        }
    }

    private void AddPlayerSlotToUI(ulong clientId)
    {
        Label playerSlot = new Label();
        playerSlot.text = clientId == NetworkManager.ServerClientId ? $"Player {clientId} (Host)" : $"Player {clientId}";
        playerSlot.AddToClassList("player-slot");
        playerListContainer.Add(playerSlot);
    }

    private void OnStartGameClicked()
    {
        if (NetworkController.Instance != null && NetworkManager.Singleton.IsServer)
        {
            NetworkController.Instance.LoadGameScene();
        }
    }

    private void OnLeaveLobbyClicked()
    {
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.DisconnectAndReturnToMenu();
        }
        ShowMainMenu();
    }

    private void OnQuitClicked()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
