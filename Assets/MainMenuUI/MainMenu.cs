using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class MainMenuUI : MonoBehaviour
{
    private UIDocument uiDocument;

    // Menu Containers
    private VisualElement menuMain;
    private VisualElement menuStartMode;
    private VisualElement menuMultiplayer;
    private VisualElement menuLobby;
    private VisualElement menuSettings;
    private VisualElement menuHelp;

    private TextField ipInput;
    private Slider volumeSlider;

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // 1. Assign Menu Containers
        menuMain = root.Q<VisualElement>("menu-main");
        menuStartMode = root.Q<VisualElement>("menu-start-mode");
        menuMultiplayer = root.Q<VisualElement>("menu-multiplayer");
        menuLobby = root.Q<VisualElement>("menu-lobby");
        menuSettings = root.Q<VisualElement>("menu-settings");
        menuHelp = root.Q<VisualElement>("menu-help");

        // 2. Assign Inputs
        ipInput = root.Q<TextField>("input-ip");
        volumeSlider = root.Q<Slider>("slider-volume");

        // 3. Bind Main Menu Buttons
        root.Q<Button>("btn-start").clicked += () => SwitchMenu(menuStartMode);
        root.Q<Button>("btn-settings").clicked += () => SwitchMenu(menuSettings);
        root.Q<Button>("btn-help").clicked += () => SwitchMenu(menuHelp);
        root.Q<Button>("btn-quit").clicked += QuitGame;

        // 4. Bind Start Mode Buttons
        root.Q<Button>("btn-singleplayer").clicked += OnSingleplayerClicked;
        root.Q<Button>("btn-multiplayer").clicked += () => SwitchMenu(menuMultiplayer);
        root.Q<Button>("btn-back-mode").clicked += () => SwitchMenu(menuMain);

        // 5. Bind Multiplayer Buttons
        root.Q<Button>("btn-create-lobby").clicked += OnCreateLobbyClicked;
        root.Q<Button>("btn-join-game").clicked += OnJoinGameClicked;
        root.Q<Button>("btn-back-multi").clicked += () => SwitchMenu(menuStartMode);

        // 6. Bind Lobby Buttons
        root.Q<Button>("btn-lobby-start").clicked += OnLobbyStartClicked;
        root.Q<Button>("btn-back-lobby").clicked += OnLeaveLobbyClicked;

        // 7. Bind Back Buttons for Settings and Help
        root.Q<Button>("btn-back-settings").clicked += () => SwitchMenu(menuMain);
        root.Q<Button>("btn-back-help").clicked += () => SwitchMenu(menuMain);

        // 8. Bind Volume Slider Logic
        volumeSlider.RegisterValueChangedCallback(evt => OnVolumeChanged(evt.newValue));

        // Ensure we start on the main menu
        SwitchMenu(menuMain);
    }

    private void SwitchMenu(VisualElement activeMenu)
    {
        // Hide all menus
        menuMain.style.display = DisplayStyle.None;
        menuStartMode.style.display = DisplayStyle.None;
        menuMultiplayer.style.display = DisplayStyle.None;
        menuLobby.style.display = DisplayStyle.None;
        menuSettings.style.display = DisplayStyle.None;
        menuHelp.style.display = DisplayStyle.None;

        // Show the requested menu
        activeMenu.style.display = DisplayStyle.Flex;
    }

    private void OnVolumeChanged(float newVolume)
    {
        // Replace with your actual audio mixer logic later
        AudioListener.volume = newVolume / 100f;
    }

    private void OnSingleplayerClicked()
    {
        Debug.Log("Starting Singleplayer...");
        if (NetworkController.Instance != null) NetworkController.Instance.StartSingleplayer();
    }

    private void OnCreateLobbyClicked()
    {
        Debug.Log("Creating Lobby...");
        SwitchMenu(menuLobby);
        if (NetworkController.Instance != null) NetworkController.Instance.HostGame();
    }

    private void OnJoinGameClicked()
    {
        string ip = ipInput.value;
        Debug.Log($"Joining Game at {ip}...");
        SwitchMenu(menuLobby);
        
        // Hide the "Start Game" button for clients in the lobby
        menuLobby.Q<Button>("btn-lobby-start").style.display = DisplayStyle.None;
        
        if (NetworkController.Instance != null) NetworkController.Instance.JoinGame(ip);
    }

    private void OnLobbyStartClicked()
    {
        Debug.Log("Host is starting the game map...");
        if (NetworkController.Instance != null) NetworkController.Instance.LoadGameScene();
    }

    private void OnLeaveLobbyClicked()
    {
        Debug.Log("Leaving Lobby...");
        SwitchMenu(menuMain);
        
        // Restore the "Start Game" button just in case we host again later
        menuLobby.Q<Button>("btn-lobby-start").style.display = DisplayStyle.Flex;
        
        if (NetworkController.Instance != null) NetworkController.Instance.DisconnectAndReturnToMenu();
    }

    private void QuitGame()
    {
        Debug.Log("Quitting Game...");
        Application.Quit();
    }
}