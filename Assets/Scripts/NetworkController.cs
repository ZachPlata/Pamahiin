using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class NetworkController : MonoBehaviour
{
    public static NetworkController Instance { get; private set; }

    [SerializeField] private string defaultIpAddress = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;

    private const string PARTY_PREF_PREFIX = "Pamahiin_PartyCode_";
    private const string PARTY_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int LAN_DISCOVERY_PORT = 47777;
    private const int MAX_RELAY_PEER_CONNECTIONS = 3; // 1 Host + up to 3 Clients = 4 Players total
    private const int MIN_PARTY_CODE_LENGTH = 5;
    private const int MAX_PARTY_CODE_LENGTH = 8;
    private const string MSG_LOADOUT_REQ = "Pamahiin_LoadoutReq";
    private const string MSG_LOADOUT_SYNC = "Pamahiin_LoadoutSync";
    private const string MSG_LOADOUT_ACK = "Pamahiin_LoadoutAck";

    private ushort activeHostPort = 7777;
    private UdpClient lanResponderUdp;
    private bool isConnectedAsRemoteClient = false;
    private bool isVoluntaryClientDisconnect = false;
    private bool reopenLobbyJournalAfterRelaySwap = false;
    private bool pendingRelaySwapFadeIn = false;
    private GameObject fallbackDisconnectOverlayObj;
    private Task unityServicesInitTask;

    public string CurrentPartyCode { get; private set; } = string.Empty;
    public bool IsMultiplayerPartyOpen { get; private set; } = false;
    public bool IsOnlineRelayParty { get; private set; } = false;
    public bool IsCreatingParty { get; private set; } = false;
    public bool IsConnectingToParty { get; private set; } = false;
    public string LastStatusMessage { get; private set; } = string.Empty;

    public event Action OnPartyStateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        ProgressionAndLoadoutManager.EnsureInstance();

        // Wait for NetworkManager to initialize
        StartCoroutine(WaitForNetworkManager());
    }

    private void OnDestroy()
    {
        StopLanResponder();
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
            nm.OnClientStopped -= OnClientStopped;
        }
    }

    private IEnumerator WaitForNetworkManager()
    {
        while (NetworkManager.Singleton == null)
        {
            yield return null;
        }

        NetworkManager.Singleton.OnServerStarted += () =>
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                if (NetworkManager.Singleton.SceneManager != null)
                {
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
                    NetworkManager.Singleton.SceneManager.OnSynchronizeComplete -= OnClientSynchronizeComplete;
                    NetworkManager.Singleton.SceneManager.OnSynchronizeComplete += OnClientSynchronizeComplete;
                }
                RegisterServerLobbyMessagingHandlers();
                ProgressionAndLoadoutManager.EnsureInstance().InitializeServerLobbyLedger();
            }
            OnPartyStateChanged?.Invoke();
        };

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnClientStopped += OnClientStopped;
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            if (nm.IsServer)
            {
                ProgressionAndLoadoutManager.EnsureInstance().ServerOnClientConnected(clientId);
                BroadcastLobbyLoadoutSync();
            }
            else if (clientId == nm.LocalClientId)
            {
                isConnectedAsRemoteClient = true;
                isVoluntaryClientDisconnect = false;
                IsConnectingToParty = false;
                LastStatusMessage = IsOnlineRelayParty
                    ? $"Joined Online Party [{CurrentPartyCode}]."
                    : $"Joined Party [{CurrentPartyCode}].";
                RegisterClientLobbyMessagingHandlers();
                ProgressionAndLoadoutManager.EnsureInstance().OnJoinedMultiplayerLobbyAsClient();
                SendLobbyLoadoutRequestToServer(0, string.Empty);
            }
        }
        OnPartyStateChanged?.Invoke();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsServer)
            {
                ProgressionAndLoadoutManager.Instance?.ServerOnClientDisconnected(clientId);
                if (!nm.ShutdownInProgress) BroadcastLobbyLoadoutSync();
            }
            else if (clientId == nm.LocalClientId || clientId == NetworkManager.ServerClientId)
            {
                bool wasConnecting = IsConnectingToParty;
                IsConnectingToParty = false;
                if (ShouldHandleUnexpectedHostDisconnect())
                {
                    HandleHostDisconnectedOnClient();
                }
                else if (clientId == nm.LocalClientId)
                {
                    isConnectedAsRemoteClient = false;
                    ProgressionAndLoadoutManager.Instance?.OnLeftMultiplayerLobbyAsClient();
                    if (wasConnecting && !isVoluntaryClientDisconnect)
                    {
                        LastStatusMessage = "Could not connect to party. Check the code or host status.";
                    }
                }
            }
        }
        OnPartyStateChanged?.Invoke();
    }

    private void OnClientStopped(bool isHost)
    {
        bool wasConnecting = !isHost && IsConnectingToParty;
        IsConnectingToParty = false;

        if (!isHost && ShouldHandleUnexpectedHostDisconnect())
        {
            HandleHostDisconnectedOnClient();
            OnPartyStateChanged?.Invoke();
        }
        else if (wasConnecting && !isVoluntaryClientDisconnect)
        {
            LastStatusMessage = "Could not connect to party. Check the code or host status.";
            OnPartyStateChanged?.Invoke();
        }
    }

    private bool ShouldHandleUnexpectedHostDisconnect()
    {
        if (isVoluntaryClientDisconnect) return false;
        bool wasRemoteClient = isConnectedAsRemoteClient ||
            (ProgressionAndLoadoutManager.Instance != null && ProgressionAndLoadoutManager.Instance.IsMultiplayerClientSession);
        if (!wasRemoteClient) return false;

        return SceneManager.GetActiveScene().name != ProgressionAndLoadoutManager.MAIN_MENU_SCENE_NAME;
    }

    private void HandleHostDisconnectedOnClient()
    {
        isConnectedAsRemoteClient = false;
        isTransitioningToGameScene = false;

        if (activeMatchIntermissionRoutine != null)
        {
            StopCoroutine(activeMatchIntermissionRoutine);
            activeMatchIntermissionRoutine = null;
        }
        if (activeClientGearVerifyRoutine != null)
        {
            StopCoroutine(activeClientGearVerifyRoutine);
            activeClientGearVerifyRoutine = null;
        }
        if (activeClientFallbackCheckRoutine != null)
        {
            StopCoroutine(activeClientFallbackCheckRoutine);
            activeClientFallbackCheckRoutine = null;
        }
        if (activeClientIntermissionFailsafeRoutine != null)
        {
            StopCoroutine(activeClientIntermissionFailsafeRoutine);
            activeClientIntermissionFailsafeRoutine = null;
        }

        EndGameRevealController.ClearPendingReveal();
        WeatherManager.SetLocalInBurialMap(false);
        DeathSequenceController.Instance?.CancelAndReset();
        GhostSpectatorVisuals.Instance?.SetSpectatorVisualsActive(false);
        ProgressionAndLoadoutManager.Instance?.OnLeftMultiplayerLobbyAsClient();

        ResetPartyState();
        LastStatusMessage = "Host disconnected.";

        LoadingScreenManager.Instance?.HideIntermission(0.1f);

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.ShutdownInProgress)
        {
            nm.Shutdown();
        }

        const string message = "The lobby host has disconnected. Please return to the Main Menu.";
        var pauseMenu = PauseMenuController.Instance ?? UnityEngine.Object.FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);
        if (pauseMenu != null)
        {
            if (!pauseMenu.gameObject.activeSelf)
            {
                pauseMenu.gameObject.SetActive(true);
            }
            pauseMenu.ShowHostDisconnected(message);
        }
        else
        {
            ShowFallbackHostDisconnectedOverlay(message);
        }
    }

    private void ShowFallbackHostDisconnectedOverlay(string message)
    {
        if (fallbackDisconnectOverlayObj != null)
        {
            Destroy(fallbackDisconnectOverlayObj);
            fallbackDisconnectOverlayObj = null;
        }

        var go = new GameObject("HostDisconnectedFallbackUI");
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = PamahiinUIThemeUtility.ResolveRuntimePanelSettings();
        doc.sortingOrder = 60;
        fallbackDisconnectOverlayObj = go;

        var root = doc.rootVisualElement;
        if (root == null) return;

        var overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0;
        overlay.style.top = 0;
        overlay.style.right = 0;
        overlay.style.bottom = 0;
        overlay.style.width = Length.Percent(100);
        overlay.style.height = Length.Percent(100);
        overlay.style.backgroundColor = new Color(6f / 255f, 4f / 255f, 8f / 255f, 0.92f);
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;

        var panel = new VisualElement();
        panel.AddToClassList("pm-panel--crimson");
        panel.style.width = 520;
        panel.style.maxWidth = Length.Percent(92);
        panel.style.paddingTop = 28;
        panel.style.paddingBottom = 30;
        panel.style.paddingLeft = 32;
        panel.style.paddingRight = 32;
        panel.style.backgroundColor = new Color(24f / 255f, 14f / 255f, 18f / 255f, 0.98f);
        panel.style.borderTopWidth = 3;
        panel.style.borderLeftWidth = 3;
        panel.style.borderRightWidth = 3;
        panel.style.borderBottomWidth = 3;
        panel.style.borderTopColor = new Color(220f / 255f, 72f / 255f, 84f / 255f, 1f);
        panel.style.borderLeftColor = new Color(220f / 255f, 72f / 255f, 84f / 255f, 1f);
        panel.style.borderRightColor = new Color(72f / 255f, 16f / 255f, 22f / 255f, 1f);
        panel.style.borderBottomColor = new Color(72f / 255f, 16f / 255f, 22f / 255f, 1f);

        var tag = new Label("CONNECTION LOST");
        tag.style.fontSize = 9;
        tag.style.color = new Color(214f / 255f, 174f / 255f, 96f / 255f, 1f);
        tag.style.unityTextAlign = TextAnchor.MiddleCenter;
        tag.style.marginBottom = 8;

        var title = new Label("HOST DISCONNECTED");
        title.style.fontSize = 22;
        title.style.color = new Color(224f / 255f, 58f / 255f, 72f / 255f, 1f);
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        title.style.marginBottom = 14;
        title.style.whiteSpace = WhiteSpace.Normal;

        var divider = new VisualElement();
        divider.style.height = 2;
        divider.style.backgroundColor = new Color(88f / 255f, 66f / 255f, 36f / 255f, 1f);
        divider.style.borderTopWidth = 1;
        divider.style.borderTopColor = new Color(166f / 255f, 132f / 255f, 74f / 255f, 1f);
        divider.style.marginBottom = 16;

        var desc = new Label(message);
        desc.style.fontSize = 10;
        desc.style.color = new Color(186f / 255f, 172f / 255f, 152f / 255f, 1f);
        desc.style.unityTextAlign = TextAnchor.MiddleCenter;
        desc.style.whiteSpace = WhiteSpace.Normal;
        desc.style.marginBottom = 22;

        var btn = new Button(DisconnectAndReturnToMenu) { text = "RETURN TO MAIN MENU" };
        btn.AddToClassList("pm-btn");
        btn.AddToClassList("pm-btn--danger");
        btn.style.fontSize = 11;
        btn.style.paddingTop = 12;
        btn.style.paddingBottom = 12;
        btn.style.paddingLeft = 14;
        btn.style.paddingRight = 14;
        btn.style.color = new Color(255f / 255f, 196f / 255f, 196f / 255f, 1f);
        btn.style.backgroundColor = new Color(88f / 255f, 18f / 255f, 24f / 255f, 0.95f);
        btn.style.borderTopWidth = 3;
        btn.style.borderLeftWidth = 3;
        btn.style.borderRightWidth = 3;
        btn.style.borderBottomWidth = 3;
        btn.style.borderTopColor = new Color(224f / 255f, 191f / 255f, 118f / 255f, 1f);
        btn.style.borderLeftColor = new Color(224f / 255f, 191f / 255f, 118f / 255f, 1f);
        btn.style.borderRightColor = new Color(72f / 255f, 16f / 255f, 22f / 255f, 1f);
        btn.style.borderBottomColor = new Color(72f / 255f, 16f / 255f, 22f / 255f, 1f);

        panel.Add(tag);
        panel.Add(title);
        panel.Add(divider);
        panel.Add(desc);
        panel.Add(btn);
        overlay.Add(panel);
        root.Add(overlay);

        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
    }

    private void OnSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (ProgressionAndLoadoutManager.IsGameplayMapScene(sceneName))
        {
            if (LoadingScreenManager.IsLoadingVisible)
            {
                LoadingScreenManager.Instance.SetProgress(0.72f, "Deploying van equipment...");
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                PlayerCorpse.DespawnAllServer();
            }

            foreach (var clientId in clientsCompleted)
            {
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    if (client.PlayerObject != null)
                    {
                        Vector3 spawnPos = PlayerSpawnPoint.GetSpawnPosition(clientId);
                        var pc = client.PlayerObject.GetComponent<PlayerController>();
                        if (pc != null)
                        {
                            if (NetworkManager.Singleton.IsServer && !pc.IsAlive)
                            {
                                pc.RevivePlayer();
                            }
                            pc.ServerTeleportTo(spawnPos);
                            pc.RestoreSanity(100f);
                        }
                        else if (client.PlayerObject.IsOwner)
                        {
                            client.PlayerObject.transform.position = spawnPos;
                        }
                    }
                }
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                StartServerEquipmentSpawnRoutine();
            }
        }
        else if (sceneName == ProgressionAndLoadoutManager.LOBBY_SCENE_NAME)
        {
            if (LoadingScreenManager.IsLoadingVisible)
            {
                LoadingScreenManager.Instance.SetProgress(0.72f, "Restoring Safe House armory...");
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                PlayerCorpse.DespawnAllServer();
            }

            var spawnPoint = UnityEngine.Object.FindAnyObjectByType<PlayerSpawnPoint>();
            Vector3 basePos = spawnPoint != null ? spawnPoint.transform.position : new Vector3(0f, -0.35f, 0f);

            int index = 0;
            foreach (var clientId in clientsCompleted)
            {
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    if (client.PlayerObject != null)
                    {
                        Vector3 spawnPos = basePos + new Vector3((index % 2 == 0 ? -0.2f : 0.2f), (index / 2) * 0.2f, 0f);
                        var pc = client.PlayerObject.GetComponent<PlayerController>();
                        if (pc != null)
                        {
                            if (client.PlayerObject.IsOwner)
                            {
                                pc.enabled = true;
                            }
                            if (NetworkManager.Singleton.IsServer && !pc.IsAlive)
                            {
                                pc.RevivePlayer();
                            }
                            pc.ServerTeleportTo(spawnPos);
                            pc.RestoreSanity(100f);
                        }
                        else if (client.PlayerObject.IsOwner)
                        {
                            client.PlayerObject.transform.position = spawnPos;
                        }
                        index++;
                    }
                }
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                BroadcastLobbyLoadoutSync();
                StartServerEquipmentSpawnRoutine();
            }

            if (pendingRelaySwapFadeIn)
            {
                pendingRelaySwapFadeIn = false;
                LoadingScreenManager.Instance?.FadeFromBlack(0.22f);
            }
        }
    }

    public bool ConsumeReopenLobbyJournalFlag()
    {
        bool flag = reopenLobbyJournalAfterRelaySwap;
        reopenLobbyJournalAfterRelaySwap = false;
        return flag;
    }

    /// <summary>
    /// Called when pressing the single "Play" button on the Main Menu.
    /// Starts a local session and spawns the player inside the Safe House Lobby Map.
    /// </summary>
    public void EnterLobbyMap()
    {
        ProgressionAndLoadoutManager.EnsureInstance();

        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
        {
            if (NetworkManager.Singleton.IsServer)
            {
                LoadLobbyScene();
                return;
            }
            isVoluntaryClientDisconnect = true;
            isConnectedAsRemoteClient = false;
            NetworkManager.Singleton.Shutdown();
        }

        isVoluntaryClientDisconnect = false;
        isConnectedAsRemoteClient = false;
        CurrentPartyCode = string.Empty;
        IsMultiplayerPartyOpen = false;
        IsOnlineRelayParty = false;
        IsCreatingParty = false;
        IsConnectingToParty = false;
        LastStatusMessage = "Solo Lobby Active";

        activeHostPort = FindAvailableUdpPort(defaultPort, 20);
        SetConnectionAddress("0.0.0.0", activeHostPort);
        NetworkManager.Singleton.StartHost();
        LoadLobbyScene();
        OnPartyStateChanged?.Invoke();
    }

    public void StartSingleplayer()
    {
        EnterLobbyMap();
    }

    /// <summary>
    /// Starts a local solo host session and loads the Warehouse Tutorial scene.
    /// </summary>
    public void EnterWarehouseTutorial()
    {
        ProgressionAndLoadoutManager.EnsureInstance();

        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
        {
            if (NetworkManager.Singleton.IsServer)
            {
                LoadWarehouseTutorialScene();
                return;
            }
            isVoluntaryClientDisconnect = true;
            isConnectedAsRemoteClient = false;
            NetworkManager.Singleton.Shutdown();
        }

        isVoluntaryClientDisconnect = false;
        isConnectedAsRemoteClient = false;
        CurrentPartyCode = string.Empty;
        IsMultiplayerPartyOpen = false;
        IsOnlineRelayParty = false;
        IsCreatingParty = false;
        IsConnectingToParty = false;
        LastStatusMessage = "Warehouse Tutorial Active";

        activeHostPort = FindAvailableUdpPort(defaultPort, 20);
        SetConnectionAddress("0.0.0.0", activeHostPort);
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartHost();
        }
        LoadWarehouseTutorialScene();
        OnPartyStateChanged?.Invoke();
    }

    public void HostGame()
    {
        if (IsCreatingParty || IsConnectingToParty) return;
        StartCoroutine(HostOnlineGameRoutine());
    }

    /// <summary>
    /// Opens Multiplayer from the current Solo Lobby (or starts an online Relay party).
    /// Returns an immediately available code if already open; otherwise updates CurrentPartyCode asynchronously via OnPartyStateChanged.
    /// </summary>
    public string CreateMultiplayerPartyCode()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            LastStatusMessage = "Only the Lobby Host can create a Party Code.";
            OnPartyStateChanged?.Invoke();
            return CurrentPartyCode;
        }

        if (!string.IsNullOrEmpty(CurrentPartyCode) && IsMultiplayerPartyOpen)
        {
            return CurrentPartyCode;
        }

        if (IsCreatingParty || IsConnectingToParty)
        {
            return CurrentPartyCode;
        }

        // If remote peers are already connected on LAN, keep the session intact and issue a LAN code in-place.
        if (NetworkManager.Singleton.ConnectedClientsIds.Count > 1)
        {
            return CreateLocalLanPartyCodeInternal("Multiplayer Open (LAN)! Share Code: ");
        }

        StartCoroutine(UpgradeSoloLobbyToOnlineRelayRoutine());
        return CurrentPartyCode;
    }

    private IEnumerator HostOnlineGameRoutine()
    {
        ProgressionAndLoadoutManager.EnsureInstance();
        IsCreatingParty = true;
        LastStatusMessage = "Connecting to Online Relay...";
        OnPartyStateChanged?.Invoke();

        var relayTask = TryAllocateRelayHostAsync(MAX_RELAY_PEER_CONNECTIONS);
        while (!relayTask.IsCompleted)
        {
            yield return null;
        }

        var allocationResult = relayTask.Result;
        if (!allocationResult.success)
        {
            IsCreatingParty = false;
            EnterLobbyMap();
            CreateLocalLanPartyCodeInternal("Relay offline — LAN Party Open! Share Code: ");
            yield break;
        }

        yield return ShutdownNetworkManagerRoutine();

        isVoluntaryClientDisconnect = false;
        isConnectedAsRemoteClient = false;
        SetRelayTransportData(allocationResult.serverData);

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.StartHost())
        {
            IsCreatingParty = false;
            EnterLobbyMap();
            CreateLocalLanPartyCodeInternal("Relay start failed — LAN Party Open! Share Code: ");
            yield break;
        }

        ApplyOpenRelayPartyState(allocationResult.joinCode);
        LoadLobbyScene();
    }

    private IEnumerator UpgradeSoloLobbyToOnlineRelayRoutine()
    {
        IsCreatingParty = true;
        LastStatusMessage = "Connecting to Online Relay...";
        OnPartyStateChanged?.Invoke();

        var relayTask = TryAllocateRelayHostAsync(MAX_RELAY_PEER_CONNECTIONS);
        while (!relayTask.IsCompleted)
        {
            yield return null;
        }

        var allocationResult = relayTask.Result;
        if (!allocationResult.success)
        {
            IsCreatingParty = false;
            CreateLocalLanPartyCodeInternal("Relay offline — LAN Party Open! Share Code: ");
            yield break;
        }

        // Guard: if a LAN peer connected while Relay was allocating, do not kick them.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkManager.Singleton.ConnectedClientsIds.Count > 1)
        {
            IsCreatingParty = false;
            CreateLocalLanPartyCodeInternal("Peer already connected — LAN Code: ");
            yield break;
        }

        // Fade to black cleanly before swapping the active Solo Host onto Relay transport.
        reopenLobbyJournalAfterRelaySwap = JournalController.IsOpen;
        var loadingMgr = LoadingScreenManager.EnsureInstance();
        if (loadingMgr != null)
        {
            pendingRelaySwapFadeIn = true;
            loadingMgr.FadeToBlack(0.16f);
            yield return new WaitForSecondsRealtime(0.16f);
        }

        TruckGearSpawner.CleanupAllEquipmentBeforeSceneChange();
        yield return ShutdownNetworkManagerRoutine();

        isVoluntaryClientDisconnect = false;
        isConnectedAsRemoteClient = false;
        SetRelayTransportData(allocationResult.serverData);

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.StartHost())
        {
            IsCreatingParty = false;
            pendingRelaySwapFadeIn = false;
            loadingMgr?.FadeFromBlack(0.2f);
            EnterLobbyMap();
            CreateLocalLanPartyCodeInternal("Relay start failed — LAN Party Open! Share Code: ");
            yield break;
        }

        ApplyOpenRelayPartyState(allocationResult.joinCode);
        LoadLobbyScene();
    }

    private void ApplyOpenRelayPartyState(string joinCode)
    {
        StopLanResponder();
        CurrentPartyCode = joinCode;
        IsMultiplayerPartyOpen = true;
        IsOnlineRelayParty = true;
        IsCreatingParty = false;
        GUIUtility.systemCopyBuffer = joinCode;
        LastStatusMessage = $"Online Party Open! Share Code: {joinCode} (Copied)";
        OnPartyStateChanged?.Invoke();
    }

    private string CreateLocalLanPartyCodeInternal(string statusPrefix)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            LastStatusMessage = "Only the Lobby Host can create a Party Code.";
            OnPartyStateChanged?.Invoke();
            return CurrentPartyCode;
        }

        // Generate a 5-character party code (first 4 random unambiguous chars + 5th char encoding port offset 0..19)
        int portOffset = Mathf.Clamp(activeHostPort - defaultPort, 0, PARTY_CHARS.Length - 1);
        char[] chars = new char[5];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = PARTY_CHARS[UnityEngine.Random.Range(0, PARTY_CHARS.Length)];
        }
        chars[4] = PARTY_CHARS[portOffset];

        CurrentPartyCode = new string(chars);
        IsMultiplayerPartyOpen = true;
        IsOnlineRelayParty = false;
        IsCreatingParty = false;

        string localIp = GetLocalIPv4();
        string endpointRecord = $"{localIp}:{activeHostPort}";
        PlayerPrefs.SetString(PARTY_PREF_PREFIX + CurrentPartyCode, endpointRecord);
        PlayerPrefs.Save();

        StartLanResponder(CurrentPartyCode, activeHostPort);
        GUIUtility.systemCopyBuffer = CurrentPartyCode;

        LastStatusMessage = $"{statusPrefix}{CurrentPartyCode}";
        OnPartyStateChanged?.Invoke();
        return CurrentPartyCode;
    }

    /// <summary>
    /// Joins another player's Multiplayer Lobby using a 5–8 character Party Code (Online Relay or LAN).
    /// </summary>
    public void JoinMultiplayerByPartyCode(string rawCode)
    {
        if (IsConnectingToParty || IsCreatingParty) return;

        if (string.IsNullOrWhiteSpace(rawCode))
        {
            LastStatusMessage = "Enter a valid Party Code (5-8 characters).";
            OnPartyStateChanged?.Invoke();
            return;
        }

        string code = rawCode.Trim().ToUpperInvariant();
        if (code.Length < MIN_PARTY_CODE_LENGTH || code.Length > MAX_PARTY_CODE_LENGTH)
        {
            LastStatusMessage = $"Party Code must be {MIN_PARTY_CODE_LENGTH}-{MAX_PARTY_CODE_LENGTH} characters long.";
            OnPartyStateChanged?.Invoke();
            return;
        }

        if (IsMultiplayerPartyOpen && string.Equals(code, CurrentPartyCode, StringComparison.OrdinalIgnoreCase))
        {
            LastStatusMessage = "You are already hosting this Party Code!";
            OnPartyStateChanged?.Invoke();
            return;
        }

        StartCoroutine(ResolveCodeAndConnectRoutine(code));
    }

    private IEnumerator ResolveCodeAndConnectRoutine(string code)
    {
        IsConnectingToParty = true;
        LastStatusMessage = $"Searching for Party [{code}]...";
        OnPartyStateChanged?.Invoke();

        bool useRelay = false;
        RelayServerData relayServerData = default;
        string targetIp = defaultIpAddress;
        ushort targetPort = defaultPort;
        bool resolvedLocally = false;

        // 1. For 5-character codes, check same-machine PlayerPrefs and LAN UDP broadcast first.
        if (code.Length == MIN_PARTY_CODE_LENGTH)
        {
            PlayerPrefs.Save();
            string savedEndpoint = PlayerPrefs.GetString(PARTY_PREF_PREFIX + code, string.Empty);
            if (!string.IsNullOrEmpty(savedEndpoint))
            {
                string[] parts = savedEndpoint.Split(':');
                if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort parsedPort))
                {
                    targetIp = "127.0.0.1";
                    targetPort = parsedPort;
                    resolvedLocally = true;
                }
            }

            if (!resolvedLocally)
            {
                string lanResult = QueryLanPartyCode(code, 250);
                if (!string.IsNullOrEmpty(lanResult))
                {
                    string[] parts = lanResult.Split(':');
                    if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort parsedPort))
                    {
                        targetIp = parts[0];
                        targetPort = parsedPort;
                        resolvedLocally = true;
                    }
                }
            }
        }

        // 2. If not resolved locally on LAN, try Online Unity Relay BEFORE tearing down the active Solo Lobby.
        if (!resolvedLocally)
        {
            LastStatusMessage = $"Joining Online Party [{code}]...";
            OnPartyStateChanged?.Invoke();

            var joinTask = TryJoinRelayAllocationAsync(code);
            while (!joinTask.IsCompleted)
            {
                yield return null;
            }

            var joinResult = joinTask.Result;
            if (joinResult.success)
            {
                useRelay = true;
                relayServerData = joinResult.serverData;
            }
            else if (code.Length > MIN_PARTY_CODE_LENGTH)
            {
                // Online Relay codes (6-8 chars) cannot fall back to LAN ports; abort safely without disturbing the Solo Lobby.
                IsConnectingToParty = false;
                LastStatusMessage = joinResult.errorMessage;
                OnPartyStateChanged?.Invoke();
                yield break;
            }
            else
            {
                // 5-char fallback: deterministic local port decoding from the 5th character
                int portOffset = PARTY_CHARS.IndexOf(code[4]);
                if (portOffset >= 0)
                {
                    targetPort = (ushort)(defaultPort + portOffset);
                }
                targetIp = defaultIpAddress;
            }
        }

        StopLanResponder();
        IsMultiplayerPartyOpen = false;
        IsOnlineRelayParty = useRelay;
        CurrentPartyCode = code;

        yield return ShutdownNetworkManagerRoutine();

        if (useRelay)
        {
            SetRelayTransportData(relayServerData);
        }
        else
        {
            SetConnectionAddress(targetIp, targetPort);
        }

        bool started = StartClientAndRegisterLobbyHandlers();
        if (!started)
        {
            IsConnectingToParty = false;
        }
        LastStatusMessage = started
            ? (useRelay ? $"Connecting to Online Party [{code}]..." : $"Connecting to Party [{code}] ({targetIp}:{targetPort})...")
            : $"Failed to connect to Party [{code}].";
        OnPartyStateChanged?.Invoke();
    }

    public void JoinGame(string ipInput)
    {
        string trimmed = string.IsNullOrWhiteSpace(ipInput) ? string.Empty : ipInput.Trim();
        if (trimmed.Length >= MIN_PARTY_CODE_LENGTH && trimmed.Length <= MAX_PARTY_CODE_LENGTH &&
            !trimmed.Contains(".") && !trimmed.Contains(":"))
        {
            JoinMultiplayerByPartyCode(trimmed);
            return;
        }

        string targetIp = string.IsNullOrEmpty(trimmed) ? defaultIpAddress : trimmed;
        ushort targetPort = defaultPort;
        int colonIdx = targetIp.LastIndexOf(':');
        if (colonIdx > 0 && ushort.TryParse(targetIp.Substring(colonIdx + 1), out ushort parsedPort))
        {
            targetPort = parsedPort;
            targetIp = targetIp.Substring(0, colonIdx);
        }

        IsOnlineRelayParty = false;
        SetConnectionAddress(targetIp, targetPort);
        StartClientAndRegisterLobbyHandlers();
    }

    private bool StartClientAndRegisterLobbyHandlers()
    {
        if (NetworkManager.Singleton == null) return false;
        isVoluntaryClientDisconnect = false;
        isConnectedAsRemoteClient = false;
        bool started = NetworkManager.Singleton.StartClient();
        if (started)
        {
            RegisterClientLobbyMessagingHandlers();
        }
        return started;
    }

    public void DisconnectAndReturnToMenu()
    {
        isVoluntaryClientDisconnect = true;
        isConnectedAsRemoteClient = false;
        if (fallbackDisconnectOverlayObj != null)
        {
            Destroy(fallbackDisconnectOverlayObj);
            fallbackDisconnectOverlayObj = null;
        }
        isTransitioningToGameScene = false;
        if (activeMatchIntermissionRoutine != null)
        {
            StopCoroutine(activeMatchIntermissionRoutine);
            activeMatchIntermissionRoutine = null;
        }
        if (activeClientIntermissionFailsafeRoutine != null)
        {
            StopCoroutine(activeClientIntermissionFailsafeRoutine);
            activeClientIntermissionFailsafeRoutine = null;
        }
        EndGameRevealController.ClearPendingReveal();
        WeatherManager.SetLocalInBurialMap(false);
        DeathSequenceController.Instance?.CancelAndReset();
        GhostSpectatorVisuals.Instance?.SetSpectatorVisualsActive(false);
        LoadingScreenManager.Instance?.HideIntermission(0.15f);
        ResetPartyState();

        string activeScene = SceneManager.GetActiveScene().name;
        bool inGameplayMatch = ProgressionAndLoadoutManager.IsGameplayMapScene(activeScene);

        if (NetworkManager.Singleton != null)
        {
            if (inGameplayMatch && (NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening))
            {
                // Ending a match early via Pause -> End Game consumes/clears the match gear so P2 items never persist into P1's save
                ProgressionAndLoadoutManager.Instance?.EnsureMatchLoadoutConsumedOnExit();
            }

            if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                ProgressionAndLoadoutManager.Instance?.OnLeftMultiplayerLobbyAsClient();
            }
            NetworkManager.Singleton.Shutdown();
        }
        else if (inGameplayMatch)
        {
            ProgressionAndLoadoutManager.Instance?.EnsureMatchLoadoutConsumedOnExit();
        }

        SceneManager.LoadScene(ProgressionAndLoadoutManager.MAIN_MENU_SCENE_NAME);
    }

    public void ReturnToLobbyFromMatch()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            ProgressionAndLoadoutManager.Instance?.EnsureMatchLoadoutConsumedOnExit();
            LoadLobbyScene();
        }
        else
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                ProgressionAndLoadoutManager.Instance?.EnsureMatchLoadoutConsumedOnExit();
            }
            EnterLobbyMap();
        }
    }

    public void KickPlayer(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer && clientId != NetworkManager.Singleton.LocalClientId)
        {
            NetworkManager.Singleton.DisconnectClient(clientId);
        }
    }

    public void LoadLobbyScene()
    {
        isTransitioningToGameScene = false;
        if (activeMatchIntermissionRoutine != null)
        {
            StopCoroutine(activeMatchIntermissionRoutine);
            activeMatchIntermissionRoutine = null;
        }

        if (EndGameRevealController.HasPendingReveal)
        {
            if (LoadingScreenManager.IsLoadingVisible)
            {
                LoadingScreenManager.Instance.SetProgress(0.58f, "Returning to Safe House...");
            }
        }
        else
        {
            LoadingScreenManager.Instance?.HideIntermission(0.2f);
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            PlayerCorpse.DespawnAllServer();
            TruckGearSpawner.CleanupAllGhostOrbs();
        }
        TruckGearSpawner.CleanupAllEquipmentBeforeSceneChange();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(ProgressionAndLoadoutManager.LOBBY_SCENE_NAME, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(ProgressionAndLoadoutManager.LOBBY_SCENE_NAME);
            if (LoadingScreenManager.IsLoadingVisible)
            {
                StartCoroutine(OfflineWaitForEquipmentThenHideRoutine());
            }
        }
    }

    public void LoadWarehouseTutorialScene()
    {
        isTransitioningToGameScene = false;
        if (activeMatchIntermissionRoutine != null)
        {
            StopCoroutine(activeMatchIntermissionRoutine);
            activeMatchIntermissionRoutine = null;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            PlayerCorpse.DespawnAllServer();
            TruckGearSpawner.CleanupAllGhostOrbs();
        }
        TruckGearSpawner.CleanupAllEquipmentBeforeSceneChange();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(ProgressionAndLoadoutManager.WAREHOUSE_TUTORIAL_SCENE_NAME, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(ProgressionAndLoadoutManager.WAREHOUSE_TUTORIAL_SCENE_NAME);
        }
    }

    public void LoadGameScene()
    {
        if (isTransitioningToGameScene) return;

        bool isListening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isListening && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        isTransitioningToGameScene = true;

        var mgr = ProgressionAndLoadoutManager.EnsureInstance();
        string targetScene = mgr != null
            ? mgr.GetSelectedSceneName()
            : ProgressionAndLoadoutManager.BARANGAY_MAP_SCENE_NAME;
        string mapDisplayName = mgr != null
            ? mgr.GetSelectedMapDisplayName()
            : "Barangay San Nicolas — 6 Tanglewood Drive";
        string difficultyName = mgr != null
            ? mgr.SelectedDifficulty.ToString().ToUpperInvariant()
            : "AMATEUR";

        ShowMatchIntermissionOverlay(mapDisplayName, difficultyName);
        if (isListening && NetworkManager.Singleton.IsServer)
        {
            BroadcastMatchLoadingIntermission(mapDisplayName, difficultyName);
        }

        if (activeMatchIntermissionRoutine != null)
        {
            StopCoroutine(activeMatchIntermissionRoutine);
        }
        activeMatchIntermissionRoutine = StartCoroutine(LoadGameSceneIntermissionRoutine(targetScene));
    }

    private void ShowMatchIntermissionOverlay(string mapDisplayName, string difficultyName)
    {
        var loadingMgr = LoadingScreenManager.EnsureInstance();
        if (loadingMgr == null) return;

        string subtitle = string.IsNullOrEmpty(difficultyName)
            ? mapDisplayName
            : $"{mapDisplayName}  •  {difficultyName}";
        loadingMgr.ShowIntermission(
            "TRAVELING TO INVESTIGATION SITE",
            subtitle,
            tag: "PAMAHIIN // FIELD DISPATCH");
        loadingMgr.SetProgress(0.18f, "Preparing Jeepney dispatch & van manifest...");
    }

    private IEnumerator LoadGameSceneIntermissionRoutine(string targetScene)
    {
        var loadingMgr = LoadingScreenManager.Instance;
        float minHold = 2.0f;
        float elapsed = 0f;
        while (elapsed < minHold)
        {
            elapsed += Time.unscaledDeltaTime;
            if (loadingMgr != null)
            {
                loadingMgr.SetProgress(
                    Mathf.Lerp(0.18f, 0.45f, Mathf.Clamp01(elapsed / minHold)),
                    "Loading Barangay transit route...");
            }
            yield return null;
        }

        TruckGearSpawner.CleanupAllEquipmentBeforeSceneChange();
        ProgressionAndLoadoutManager.Instance?.MarkMatchLoadoutActive();

        if (loadingMgr != null)
        {
            loadingMgr.SetProgress(0.55f, "Entering investigation sector...");
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
        {
            // NGO synchronized scene loading across all connected clients
            NetworkManager.Singleton.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
        }
        else
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
            if (asyncLoad != null)
            {
                while (!asyncLoad.isDone)
                {
                    if (LoadingScreenManager.Instance != null)
                    {
                        float p = Mathf.Clamp01(asyncLoad.progress / 0.9f);
                        LoadingScreenManager.Instance.SetProgress(
                            Mathf.Lerp(0.55f, 0.72f, p),
                            "Entering investigation sector...");
                    }
                    yield return null;
                }
            }
            else
            {
                SceneManager.LoadScene(targetScene);
            }
            StartCoroutine(OfflineWaitForEquipmentThenHideRoutine());
        }

        activeMatchIntermissionRoutine = null;
        isTransitioningToGameScene = false;
    }

    private IEnumerator OfflineWaitForEquipmentThenHideRoutine()
    {
        yield return null;
        yield return null;

        yield return WaitForGameplayMapWarmupRoutine(requireServerLoadoutSpawn: true);

        if (LoadingScreenManager.IsLoadingVisible)
        {
            LoadingScreenManager.Instance.SetProgress(1f, "Equipment manifest ready.");
            yield return new WaitForSecondsRealtime(0.18f);
            FinishLoadingIntermission("Equipment manifest ready.");
        }
    }

    private IEnumerator WaitForGameplayMapWarmupRoutine(bool requireServerLoadoutSpawn)
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (!ProgressionAndLoadoutManager.IsGameplayMapScene(activeScene))
        {
            yield break;
        }

        if (LoadingScreenManager.IsLoadingVisible)
        {
            LoadingScreenManager.Instance.SetProgress(0.78f, "Deploying van equipment...");
        }

        float elapsed = 0f;
        while (elapsed < 2.5f)
        {
            bool gearReady = TruckGearSpawner.Instance != null &&
                             (!requireServerLoadoutSpawn || TruckGearSpawner.Instance.HasSpawnedLoadout);
            if (gearReady) break;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (LoadingScreenManager.IsLoadingVisible)
        {
            LoadingScreenManager.Instance.SetProgress(0.88f, "Stabilizing Barangay weather & lighting...");
        }

        elapsed = 0f;
        while (elapsed < 1.5f)
        {
            if (WeatherManager.Instance != null && WeatherManager.Instance.IsWarmupReady)
            {
                break;
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (LoadingScreenManager.IsLoadingVisible)
        {
            LoadingScreenManager.Instance.SetProgress(0.95f, "Initializing investigation sector...");
        }

        elapsed = 0f;
        while (elapsed < 1.5f)
        {
            if (GameMatchManager.Instance != null)
            {
                break;
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Allow 2 unscaled frames for 2D lighting, shaders, and UI layout to settle behind the overlay
        yield return null;
        yield return null;
    }

    private void ResetPartyState()
    {
        StopLanResponder();
        CurrentPartyCode = string.Empty;
        IsMultiplayerPartyOpen = false;
        IsOnlineRelayParty = false;
        IsCreatingParty = false;
        IsConnectingToParty = false;
        reopenLobbyJournalAfterRelaySwap = false;
        pendingRelaySwapFadeIn = false;
    }

    private IEnumerator ShutdownNetworkManagerRoutine()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || (!nm.IsServer && !nm.IsClient && !nm.IsListening))
        {
            yield break;
        }

        isVoluntaryClientDisconnect = true;
        isConnectedAsRemoteClient = false;
        nm.Shutdown();

        float waitTimer = 0f;
        while (nm.ShutdownInProgress && waitTimer < 2f)
        {
            waitTimer += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return null;
    }

    private void SetConnectionAddress(string ip, ushort port)
    {
        if (NetworkManager.Singleton == null) return;
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(ip, port);
        }
    }

    private void SetRelayTransportData(RelayServerData serverData)
    {
        if (NetworkManager.Singleton == null) return;
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetRelayServerData(serverData);
        }
    }

    private Task EnsureUnityServicesSignedInAsync()
    {
        if (unityServicesInitTask == null || unityServicesInitTask.IsFaulted || unityServicesInitTask.IsCanceled)
        {
            unityServicesInitTask = InitializeServicesAndSignInInternalAsync();
        }
        return unityServicesInitTask;
    }

    private static async Task InitializeServicesAndSignInInternalAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private async Task<(bool success, string joinCode, RelayServerData serverData, string errorMessage)> TryAllocateRelayHostAsync(int maxPeerConnections)
    {
        try
        {
            await EnsureUnityServicesSignedInAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPeerConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return (false, string.Empty, default, "Relay returned an empty Join Code.");
            }

            RelayServerData serverData = BuildRelayServerData(allocation);
            return (true, joinCode.Trim().ToUpperInvariant(), serverData, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NetworkController] Unity Relay host allocation unavailable, falling back to LAN/local: {ex.Message}");
            return (false, string.Empty, default, ex.Message);
        }
    }

    private async Task<(bool success, RelayServerData serverData, string errorMessage)> TryJoinRelayAllocationAsync(string joinCode)
    {
        try
        {
            await EnsureUnityServicesSignedInAsync();
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            RelayServerData serverData = BuildRelayServerData(joinAllocation);
            return (true, serverData, string.Empty);
        }
        catch (RelayServiceException relayEx)
        {
            Debug.LogWarning($"[NetworkController] Relay join failed for [{joinCode}] ({relayEx.Reason}): {relayEx.Message}");
            string msg = relayEx.Reason == RelayExceptionReason.JoinCodeNotFound
                ? $"Party [{joinCode}] was not found or has closed."
                : $"Could not join Online Party [{joinCode}]: {relayEx.Reason}";
            return (false, default, msg);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NetworkController] Online Relay join unavailable for [{joinCode}]: {ex.Message}");
            return (false, default, $"Offline or Relay unreachable for [{joinCode}].");
        }
    }

    private static RelayServerData BuildRelayServerData(Allocation allocation)
    {
        try
        {
            return allocation.ToRelayServerData("dtls");
        }
        catch
        {
            return allocation.ToRelayServerData("udp");
        }
    }

    private static RelayServerData BuildRelayServerData(JoinAllocation joinAllocation)
    {
        try
        {
            return joinAllocation.ToRelayServerData("dtls");
        }
        catch
        {
            return joinAllocation.ToRelayServerData("udp");
        }
    }

    private static ushort FindAvailableUdpPort(ushort startPort, int maxAttempts)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            ushort candidate = (ushort)(startPort + i);
            try
            {
                using (var udp = new UdpClient(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Port is busy, try next
            }
        }
        return startPort;
    }

    private static string GetLocalIPv4()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private void StartLanResponder(string partyCode, ushort hostPort)
    {
        StopLanResponder();
        try
        {
            lanResponderUdp = new UdpClient();
            lanResponderUdp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            lanResponderUdp.Client.Bind(new IPEndPoint(IPAddress.Any, LAN_DISCOVERY_PORT));
            lanResponderUdp.BeginReceive(OnLanDiscoveryReceived, null);
        }
        catch
        {
            // Ignore if port 47777 is already bound on this machine
        }
    }

    private void OnLanDiscoveryReceived(IAsyncResult ar)
    {
        if (lanResponderUdp == null) return;
        try
        {
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            byte[] data = lanResponderUdp.EndReceive(ar, ref remoteEp);
            string msg = Encoding.UTF8.GetString(data);
            if (!string.IsNullOrEmpty(CurrentPartyCode) && msg == "PAMAHIIN_QUERY:" + CurrentPartyCode)
            {
                string reply = $"PAMAHIIN_HOST:{GetLocalIPv4()}:{activeHostPort}";
                byte[] replyBytes = Encoding.UTF8.GetBytes(reply);
                lanResponderUdp.Send(replyBytes, replyBytes.Length, remoteEp);
            }
            lanResponderUdp.BeginReceive(OnLanDiscoveryReceived, null);
        }
        catch { }
    }

    private void StopLanResponder()
    {
        if (lanResponderUdp != null)
        {
            try { lanResponderUdp.Close(); } catch { }
            lanResponderUdp = null;
        }
    }

    private static string QueryLanPartyCode(string partyCode, int timeoutMs)
    {
        try
        {
            using (UdpClient client = new UdpClient())
            {
                client.EnableBroadcast = true;
                client.Client.ReceiveTimeout = timeoutMs;
                byte[] req = Encoding.UTF8.GetBytes("PAMAHIIN_QUERY:" + partyCode);
                client.Send(req, req.Length, new IPEndPoint(IPAddress.Broadcast, LAN_DISCOVERY_PORT));

                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] resp = client.Receive(ref sender);
                string text = Encoding.UTF8.GetString(resp);
                if (text.StartsWith("PAMAHIIN_HOST:"))
                {
                    return text.Substring("PAMAHIIN_HOST:".Length);
                }
            }
        }
        catch { }
        return null;
    }

    #region Lightweight Lobby Loadout Sync Messaging

    private const string MSG_VAN_GEAR_MANIFEST = "Pamahiin_VanGearManifest";
    private const string MSG_VAN_GEAR_RESYNC = "Pamahiin_VanGearResync";
    private const string MSG_MATCH_LOADING_INTERMISSION = "Pamahiin_MatchLoadingIntermission";

    private Coroutine activeServerEquipmentSpawnRoutine;
    private Coroutine activeClientGearVerifyRoutine;
    private Coroutine activeClientFallbackCheckRoutine;
    private Coroutine activeMatchIntermissionRoutine;
    private Coroutine activeClientIntermissionFailsafeRoutine;
    private bool hasReceivedVanManifestThisScene;
    private bool isTransitioningToGameScene;

    private void StartServerEquipmentSpawnRoutine()
    {
        if (activeServerEquipmentSpawnRoutine != null)
        {
            StopCoroutine(activeServerEquipmentSpawnRoutine);
        }
        activeServerEquipmentSpawnRoutine = StartCoroutine(SpawnSceneEquipmentWhenReadyRoutine());
    }

    private IEnumerator SpawnSceneEquipmentWhenReadyRoutine()
    {
        // Wait briefly past OnLoadEventCompleted so every client's SynchronizeSceneNetworkObjects and local hooks have settled.
        yield return null;
        yield return new WaitForEndOfFrame();

        activeServerEquipmentSpawnRoutine = null;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer)
        {
            FinishLoadingIntermission("Ready.");
            yield break;
        }

        var spawner = TruckGearSpawner.EnsureSpawnerAndSpawnOnServer();
        if (spawner != null)
        {
            BroadcastVanGearManifest();
        }

        yield return WaitForGameplayMapWarmupRoutine(requireServerLoadoutSpawn: true);

        if (LoadingScreenManager.IsLoadingVisible)
        {
            LoadingScreenManager.Instance.SetProgress(1f, "Van equipment ready.");
            yield return new WaitForSecondsRealtime(0.2f);
            FinishLoadingIntermission("Van equipment ready.");
        }
    }

    private void FinishLoadingIntermission(string statusText)
    {
        if (EndGameRevealController.HasPendingReveal &&
            SceneManager.GetActiveScene().name == ProgressionAndLoadoutManager.LOBBY_SCENE_NAME)
        {
            EndGameRevealController.TryPresentPendingReveal();
        }

        if (LoadingScreenManager.IsLoadingVisible)
        {
            LoadingScreenManager.Instance.SetProgress(1f, statusText);
            LoadingScreenManager.Instance.HideIntermission(0.38f);
        }
    }

    private void OnClientSynchronizeComplete(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer || clientId == NetworkManager.ServerClientId) return;

        if (TruckGearSpawner.Instance != null && TruckGearSpawner.Instance.HasSpawnedLoadout)
        {
            StartCoroutine(SendManifestToClientDelayedRoutine(clientId));
        }
    }

    private IEnumerator SendManifestToClientDelayedRoutine(ulong clientId)
    {
        yield return null;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer) yield break;
        if (!nm.ConnectedClients.ContainsKey(clientId)) yield break;

        SendVanGearManifestToClients(new[] { clientId });
    }

    private void RegisterServerLobbyMessagingHandlers()
    {
        var cm = NetworkManager.Singleton != null ? NetworkManager.Singleton.CustomMessagingManager : null;
        if (cm == null) return;
        cm.UnregisterNamedMessageHandler(MSG_LOADOUT_REQ);
        cm.RegisterNamedMessageHandler(MSG_LOADOUT_REQ, OnLoadoutRequestReceived);
        cm.UnregisterNamedMessageHandler(MSG_VAN_GEAR_RESYNC);
        cm.RegisterNamedMessageHandler(MSG_VAN_GEAR_RESYNC, OnVanGearResyncReceived);
    }

    private void RegisterClientLobbyMessagingHandlers()
    {
        var nm = NetworkManager.Singleton;
        var cm = nm != null ? nm.CustomMessagingManager : null;
        if (cm != null)
        {
            cm.UnregisterNamedMessageHandler(MSG_LOADOUT_SYNC);
            cm.RegisterNamedMessageHandler(MSG_LOADOUT_SYNC, OnLoadoutSyncReceived);
            cm.UnregisterNamedMessageHandler(MSG_LOADOUT_ACK);
            cm.RegisterNamedMessageHandler(MSG_LOADOUT_ACK, OnLoadoutAckReceived);
            cm.UnregisterNamedMessageHandler(MSG_VAN_GEAR_MANIFEST);
            cm.RegisterNamedMessageHandler(MSG_VAN_GEAR_MANIFEST, OnVanGearManifestReceived);
            cm.UnregisterNamedMessageHandler(MSG_MATCH_LOADING_INTERMISSION);
            cm.RegisterNamedMessageHandler(MSG_MATCH_LOADING_INTERMISSION, OnMatchLoadingIntermissionReceived);
        }

        if (nm != null && nm.SceneManager != null)
        {
            nm.SceneManager.OnSceneEvent -= OnClientNetworkSceneEvent;
            nm.SceneManager.OnSceneEvent += OnClientNetworkSceneEvent;
        }
    }

    private void BroadcastMatchLoadingIntermission(string mapDisplayName, string difficultyName)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer || nm.ShutdownInProgress || nm.CustomMessagingManager == null) return;

        List<ulong> remoteClients = CollectRemoteClientIds(nm);
        if (remoteClients.Count == 0) return;

        using (var writer = new FastBufferWriter(256, Allocator.Temp, 2048))
        {
            writer.WriteValueSafe(mapDisplayName ?? string.Empty);
            writer.WriteValueSafe(difficultyName ?? string.Empty);
            nm.CustomMessagingManager.SendNamedMessage(MSG_MATCH_LOADING_INTERMISSION, remoteClients, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void OnMatchLoadingIntermissionReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) return;

        reader.ReadValueSafe(out string mapDisplayName);
        reader.ReadValueSafe(out string difficultyName);
        ShowMatchIntermissionOverlay(mapDisplayName, difficultyName);
        if (LoadingScreenManager.IsLoadingVisible)
        {
            LoadingScreenManager.Instance.SetProgress(0.28f, "Host initiated deployment...");
        }

        if (activeClientIntermissionFailsafeRoutine != null)
        {
            StopCoroutine(activeClientIntermissionFailsafeRoutine);
        }
        activeClientIntermissionFailsafeRoutine = StartCoroutine(ClientIntermissionFailsafeRoutine());
    }

    private IEnumerator ClientIntermissionFailsafeRoutine()
    {
        yield return new WaitForSecondsRealtime(12f);
        activeClientIntermissionFailsafeRoutine = null;
        if (LoadingScreenManager.IsLoadingVisible)
        {
            FinishLoadingIntermission("Ready.");
        }
    }

    private void OnClientNetworkSceneEvent(SceneEvent sceneEvent)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer) return;
        if (sceneEvent.ClientId != nm.LocalClientId) return;

        if (sceneEvent.SceneEventType == SceneEventType.Load)
        {
            hasReceivedVanManifestThisScene = false;
            if (activeClientGearVerifyRoutine != null) StopCoroutine(activeClientGearVerifyRoutine);
            if (activeClientFallbackCheckRoutine != null) StopCoroutine(activeClientFallbackCheckRoutine);
            if (LoadingScreenManager.IsLoadingVisible)
            {
                string loadMsg = EndGameRevealController.HasPendingReveal
                    ? "Returning to Safe House Lobby..."
                    : "Traveling to investigation site...";
                LoadingScreenManager.Instance.SetProgress(0.55f, loadMsg);
            }
            return;
        }

        if (sceneEvent.SceneEventType == SceneEventType.LoadComplete ||
            sceneEvent.SceneEventType == SceneEventType.SynchronizeComplete)
        {
            if (LoadingScreenManager.IsLoadingVisible)
            {
                string syncMsg = EndGameRevealController.HasPendingReveal
                    ? "Restoring Safe House armory..."
                    : "Synchronizing van equipment...";
                LoadingScreenManager.Instance.SetProgress(0.80f, syncMsg);
            }
            if (activeClientFallbackCheckRoutine != null)
            {
                StopCoroutine(activeClientFallbackCheckRoutine);
            }
            activeClientFallbackCheckRoutine = StartCoroutine(ClientFallbackVanGearPresenceCheckRoutine());
        }
    }

    private IEnumerator ClientFallbackVanGearPresenceCheckRoutine()
    {
        yield return new WaitForSecondsRealtime(0.9f);
        activeClientFallbackCheckRoutine = null;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer) yield break;
        if (hasReceivedVanManifestThisScene)
        {
            if (activeClientGearVerifyRoutine == null)
            {
                yield return WaitForGameplayMapWarmupRoutine(requireServerLoadoutSpawn: false);
                FinishLoadingIntermission("Van equipment ready.");
            }
            yield break;
        }

        string activeScene = SceneManager.GetActiveScene().name;
        if (ProgressionAndLoadoutManager.IsGameplayMapScene(activeScene) ||
            activeScene == ProgressionAndLoadoutManager.LOBBY_SCENE_NAME)
        {
            // Ask server for the manifest if client somehow missed it or finished late
            SendVanGearResyncRequest(Array.Empty<ulong>());
        }

        yield return new WaitForSecondsRealtime(0.8f);
        string finalScene = SceneManager.GetActiveScene().name;
        if (LoadingScreenManager.IsLoadingVisible &&
            (ProgressionAndLoadoutManager.IsGameplayMapScene(finalScene) || finalScene == ProgressionAndLoadoutManager.LOBBY_SCENE_NAME))
        {
            yield return WaitForGameplayMapWarmupRoutine(requireServerLoadoutSpawn: false);
            FinishLoadingIntermission("Arrival complete.");
        }
    }

    public void SendLobbyLoadoutRequestToServer(byte op, string equipmentId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsClient || nm.CustomMessagingManager == null) return;

        using (var writer = new FastBufferWriter(128, Allocator.Temp, 1024))
        {
            writer.WriteValueSafe(op);
            writer.WriteValueSafe(equipmentId ?? string.Empty);
            nm.CustomMessagingManager.SendNamedMessage(MSG_LOADOUT_REQ, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    public void BroadcastLobbyLoadoutSync()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer) return;

        List<ulong> remoteClients = CollectRemoteClientIds(nm);
        SendLobbyLoadoutSyncToClients(remoteClients);
    }

    private static List<ulong> CollectRemoteClientIds(NetworkManager nm)
    {
        var remoteClients = new List<ulong>();
        foreach (var cid in nm.ConnectedClientsIds)
        {
            if (cid != NetworkManager.ServerClientId)
            {
                remoteClients.Add(cid);
            }
        }
        return remoteClients;
    }

    private void SendLobbyLoadoutSyncToClients(IReadOnlyList<ulong> targetClientIds)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer || nm.ShutdownInProgress || nm.CustomMessagingManager == null) return;
        if (targetClientIds == null || targetClientIds.Count == 0) return;

        var mgr = ProgressionAndLoadoutManager.EnsureInstance();
        using (var writer = new FastBufferWriter(512, Allocator.Temp, 4096))
        {
            mgr.WriteSyncSnapshot(writer);
            nm.CustomMessagingManager.SendNamedMessage(MSG_LOADOUT_SYNC, targetClientIds, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void SendLoadoutAckToClient(ulong clientId, byte op, string equipmentId, bool approved)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer || nm.CustomMessagingManager == null) return;

        using (var writer = new FastBufferWriter(128, Allocator.Temp, 1024))
        {
            writer.WriteValueSafe(op);
            writer.WriteValueSafe(equipmentId ?? string.Empty);
            writer.WriteValueSafe(approved);
            nm.CustomMessagingManager.SendNamedMessage(MSG_LOADOUT_ACK, clientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void OnLoadoutRequestReceived(ulong senderClientId, FastBufferReader reader)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        reader.ReadValueSafe(out byte op);
        reader.ReadValueSafe(out string equipmentId);

        var mgr = ProgressionAndLoadoutManager.EnsureInstance();
        mgr.ServerOnClientConnected(senderClientId);

        if (op == 0)
        {
            if (senderClientId != NetworkManager.ServerClientId)
            {
                SendLobbyLoadoutSyncToClients(new[] { senderClientId });
            }
            return;
        }

        if (op == 1)
        {
            bool approved = mgr.ServerProcessBuyRequest(senderClientId, equipmentId);
            SendLoadoutAckToClient(senderClientId, op, equipmentId, approved);
            BroadcastLobbyLoadoutSync();
            return;
        }

        if (op == 2)
        {
            bool approved = mgr.ServerProcessRemoveRequest(senderClientId, equipmentId);
            SendLoadoutAckToClient(senderClientId, op, equipmentId, approved);
            BroadcastLobbyLoadoutSync();
            return;
        }

        if (op == 3)
        {
            bool approved = mgr.ServerProcessReliefFlashlightRequest(senderClientId);
            SendLoadoutAckToClient(senderClientId, op, equipmentId, approved);
            BroadcastLobbyLoadoutSync();
        }
    }

    private void OnLoadoutSyncReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) return;
        ProgressionAndLoadoutManager.EnsureInstance().ApplySyncSnapshot(reader);
    }

    private void OnLoadoutAckReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) return;
        reader.ReadValueSafe(out byte op);
        reader.ReadValueSafe(out string equipmentId);
        reader.ReadValueSafe(out bool approved);
        ProgressionAndLoadoutManager.EnsureInstance().HandleClientPurchaseAck(op, equipmentId, approved);
    }

    private void BroadcastVanGearManifest()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer) return;

        List<ulong> remoteClients = CollectRemoteClientIds(nm);
        SendVanGearManifestToClients(remoteClients);
    }

    private void SendVanGearManifestToClients(IReadOnlyList<ulong> targetClientIds)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer || nm.ShutdownInProgress || nm.CustomMessagingManager == null) return;
        if (targetClientIds == null || targetClientIds.Count == 0) return;

        List<ulong> activeIds = TruckGearSpawner.GetActiveSpawnedEquipmentNetworkIds();
        int cap = Mathf.Min(activeIds.Count, ushort.MaxValue);
        using (var writer = new FastBufferWriter(64 + cap * 8, Allocator.Temp, 4096))
        {
            writer.WriteValueSafe((ushort)cap);
            for (int i = 0; i < cap; i++)
            {
                writer.WriteValueSafe(activeIds[i]);
            }
            nm.CustomMessagingManager.SendNamedMessage(MSG_VAN_GEAR_MANIFEST, targetClientIds, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void OnVanGearManifestReceived(ulong senderClientId, FastBufferReader reader)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsServer) return;

        reader.ReadValueSafe(out ushort count);
        ulong[] expectedIds = new ulong[count];
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out expectedIds[i]);
        }

        hasReceivedVanManifestThisScene = true;
        if (activeClientGearVerifyRoutine != null)
        {
            StopCoroutine(activeClientGearVerifyRoutine);
        }
        activeClientGearVerifyRoutine = StartCoroutine(VerifyClientVanGearRoutine(expectedIds));
    }

    private IEnumerator VerifyClientVanGearRoutine(ulong[] expectedIds)
    {
        if (expectedIds == null || expectedIds.Length == 0)
        {
            yield return WaitForGameplayMapWarmupRoutine(requireServerLoadoutSpawn: false);
            activeClientGearVerifyRoutine = null;
            if (activeClientIntermissionFailsafeRoutine != null)
            {
                StopCoroutine(activeClientIntermissionFailsafeRoutine);
                activeClientIntermissionFailsafeRoutine = null;
            }
            FinishLoadingIntermission("Van equipment ready.");
            yield break;
        }

        float[] delays = { 0.25f, 0.75f };
        for (int attempt = 0; attempt < delays.Length; attempt++)
        {
            yield return new WaitForSecondsRealtime(delays[attempt]);

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.IsServer || nm.SpawnManager == null)
            {
                activeClientGearVerifyRoutine = null;
                yield break;
            }

            List<ulong> missing = CollectMissingNetworkObjectIds(nm, expectedIds);
            if (missing.Count == 0)
            {
                yield return WaitForGameplayMapWarmupRoutine(requireServerLoadoutSpawn: false);
                activeClientGearVerifyRoutine = null;
                if (activeClientIntermissionFailsafeRoutine != null)
                {
                    StopCoroutine(activeClientIntermissionFailsafeRoutine);
                    activeClientIntermissionFailsafeRoutine = null;
                }
                FinishLoadingIntermission("Van equipment ready.");
                yield break;
            }

            SendVanGearResyncRequest(missing.ToArray());
        }

        yield return WaitForGameplayMapWarmupRoutine(requireServerLoadoutSpawn: false);
        activeClientGearVerifyRoutine = null;
        if (activeClientIntermissionFailsafeRoutine != null)
        {
            StopCoroutine(activeClientIntermissionFailsafeRoutine);
            activeClientIntermissionFailsafeRoutine = null;
        }
        FinishLoadingIntermission("Arrival complete.");
    }

    private static List<ulong> CollectMissingNetworkObjectIds(NetworkManager nm, ulong[] expectedIds)
    {
        var missing = new List<ulong>();
        for (int i = 0; i < expectedIds.Length; i++)
        {
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(expectedIds[i], out var obj) || obj == null)
            {
                missing.Add(expectedIds[i]);
            }
        }
        return missing;
    }

    private void SendVanGearResyncRequest(ulong[] missingIds)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsClient || nm.CustomMessagingManager == null) return;

        ushort count = (ushort)(missingIds != null ? Mathf.Min(missingIds.Length, ushort.MaxValue) : 0);
        using (var writer = new FastBufferWriter(64 + count * 8, Allocator.Temp, 4096))
        {
            writer.WriteValueSafe(count);
            for (int i = 0; i < count; i++)
            {
                writer.WriteValueSafe(missingIds[i]);
            }
            nm.CustomMessagingManager.SendNamedMessage(MSG_VAN_GEAR_RESYNC, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void OnVanGearResyncReceived(ulong senderClientId, FastBufferReader reader)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer) return;

        reader.ReadValueSafe(out ushort missingCount);
        if (missingCount == 0)
        {
            SendVanGearManifestToClients(new[] { senderClientId });
            return;
        }

        ulong[] requestedIds = new ulong[missingCount];
        for (int i = 0; i < missingCount; i++)
        {
            reader.ReadValueSafe(out requestedIds[i]);
        }
        StartCoroutine(ReshowEquipmentToClientRoutine(senderClientId, requestedIds));
    }

    private IEnumerator ReshowEquipmentToClientRoutine(ulong clientId, ulong[] objectIds)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer || nm.SpawnManager == null) yield break;
        if (!nm.ConnectedClients.ContainsKey(clientId)) yield break;

        var toHide = new List<NetworkObject>();
        var toShow = new List<NetworkObject>();
        for (int i = 0; i < objectIds.Length; i++)
        {
            if (nm.SpawnManager.SpawnedObjects.TryGetValue(objectIds[i], out var netObj) && netObj != null && netObj.IsSpawned)
            {
                toShow.Add(netObj);
                if (netObj.IsNetworkVisibleTo(clientId))
                {
                    toHide.Add(netObj);
                }
            }
        }

        if (toHide.Count > 0)
        {
            NetworkObject.NetworkHide(toHide, clientId);
            yield return null;
        }

        if (nm == null || !nm.IsListening || !nm.IsServer || !nm.ConnectedClients.ContainsKey(clientId)) yield break;

        // Prune any objects destroyed while waiting 1 frame before reshowing
        for (int i = toShow.Count - 1; i >= 0; i--)
        {
            if (toShow[i] == null || !toShow[i].IsSpawned)
            {
                toShow.RemoveAt(i);
            }
        }

        if (toShow.Count > 0)
        {
            NetworkObject.NetworkShow(toShow, clientId);
        }
    }

    #endregion
}
