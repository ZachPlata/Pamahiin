using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkController : MonoBehaviour
{
    public static NetworkController Instance { get; private set; }

    [SerializeField] private string defaultIpAddress = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;

    private const string PARTY_PREF_PREFIX = "Pamahiin_PartyCode_";
    private const string PARTY_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int LAN_DISCOVERY_PORT = 47777;
    private const string MSG_LOADOUT_REQ = "Pamahiin_LoadoutReq";
    private const string MSG_LOADOUT_SYNC = "Pamahiin_LoadoutSync";
    private const string MSG_LOADOUT_ACK = "Pamahiin_LoadoutAck";

    private ushort activeHostPort = 7777;
    private UdpClient lanResponderUdp;

    public string CurrentPartyCode { get; private set; } = string.Empty;
    public bool IsMultiplayerPartyOpen { get; private set; } = false;
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
            else if (clientId == nm.LocalClientId)
            {
                ProgressionAndLoadoutManager.Instance?.OnLeftMultiplayerLobbyAsClient();
            }
        }
        OnPartyStateChanged?.Invoke();
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
        }
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
            NetworkManager.Singleton.Shutdown();
        }

        CurrentPartyCode = string.Empty;
        IsMultiplayerPartyOpen = false;
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

    public void HostGame()
    {
        EnterLobbyMap();
        CreateMultiplayerPartyCode();
    }

    /// <summary>
    /// Creates a 5-character alphanumeric Party Code so other players can join this Lobby Map.
    /// </summary>
    public string CreateMultiplayerPartyCode()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            LastStatusMessage = "Only the Lobby Host can create a Party Code.";
            OnPartyStateChanged?.Invoke();
            return CurrentPartyCode;
        }

        if (!string.IsNullOrEmpty(CurrentPartyCode) && CurrentPartyCode.Length == 5 && IsMultiplayerPartyOpen)
        {
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

        string localIp = GetLocalIPv4();
        string endpointRecord = $"{localIp}:{activeHostPort}";
        PlayerPrefs.SetString(PARTY_PREF_PREFIX + CurrentPartyCode, endpointRecord);
        PlayerPrefs.Save();

        StartLanResponder(CurrentPartyCode, activeHostPort);

        LastStatusMessage = $"Multiplayer Open! Share Code: {CurrentPartyCode}";
        OnPartyStateChanged?.Invoke();
        return CurrentPartyCode;
    }

    /// <summary>
    /// Joins another player's Multiplayer Lobby using their 5-character Party Code.
    /// </summary>
    public void JoinMultiplayerByPartyCode(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            LastStatusMessage = "Enter a valid 5-character Party Code.";
            OnPartyStateChanged?.Invoke();
            return;
        }

        string code = rawCode.Trim().ToUpperInvariant();
        if (code.Length != 5)
        {
            LastStatusMessage = "Party Code must be 5 characters long.";
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
        LastStatusMessage = $"Searching for Party [{code}]...";
        OnPartyStateChanged?.Invoke();

        string targetIp = "127.0.0.1";
        ushort targetPort = defaultPort;
        bool resolved = false;

        // 1. Check same-machine PlayerPrefs registry first
        PlayerPrefs.Save();
        string savedEndpoint = PlayerPrefs.GetString(PARTY_PREF_PREFIX + code, string.Empty);
        if (!string.IsNullOrEmpty(savedEndpoint))
        {
            string[] parts = savedEndpoint.Split(':');
            if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort parsedPort))
            {
                targetIp = "127.0.0.1";
                targetPort = parsedPort;
                resolved = true;
            }
        }

        // 2. Try quick UDP LAN broadcast query if not resolved locally
        if (!resolved)
        {
            string lanResult = QueryLanPartyCode(code, 250);
            if (!string.IsNullOrEmpty(lanResult))
            {
                string[] parts = lanResult.Split(':');
                if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort parsedPort))
                {
                    targetIp = parts[0];
                    targetPort = parsedPort;
                    resolved = true;
                }
            }
        }

        // 3. Fallback deterministic port decoding from the 5th character of the Party Code
        if (!resolved)
        {
            int portOffset = PARTY_CHARS.IndexOf(code[4]);
            if (portOffset >= 0)
            {
                targetPort = (ushort)(defaultPort + portOffset);
            }
            targetIp = defaultIpAddress;
        }

        StopLanResponder();
        IsMultiplayerPartyOpen = false;
        CurrentPartyCode = code;

        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
        {
            NetworkManager.Singleton.Shutdown();
            float waitTimer = 0f;
            while (NetworkManager.Singleton.ShutdownInProgress && waitTimer < 2f)
            {
                waitTimer += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return null;
        }

        SetConnectionAddress(targetIp, targetPort);
        bool started = StartClientAndRegisterLobbyHandlers();
        LastStatusMessage = started
            ? $"Connecting to Party [{code}] ({targetIp}:{targetPort})..."
            : $"Failed to connect to Party [{code}].";
        OnPartyStateChanged?.Invoke();
    }

    public void JoinGame(string ipInput)
    {
        if (!string.IsNullOrEmpty(ipInput) && ipInput.Trim().Length == 5 && !ipInput.Contains("."))
        {
            JoinMultiplayerByPartyCode(ipInput);
            return;
        }

        string targetIp = string.IsNullOrEmpty(ipInput) ? defaultIpAddress : ipInput;
        SetConnectionAddress(targetIp, defaultPort);
        StartClientAndRegisterLobbyHandlers();
    }

    private bool StartClientAndRegisterLobbyHandlers()
    {
        if (NetworkManager.Singleton == null) return false;
        bool started = NetworkManager.Singleton.StartClient();
        if (started)
        {
            RegisterClientLobbyMessagingHandlers();
        }
        return started;
    }

    public void DisconnectAndReturnToMenu()
    {
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
        StopLanResponder();
        CurrentPartyCode = string.Empty;
        IsMultiplayerPartyOpen = false;

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
            SceneManager.LoadScene(targetScene);
            StartCoroutine(OfflineWaitForEquipmentThenHideRoutine());
        }

        activeMatchIntermissionRoutine = null;
        isTransitioningToGameScene = false;
    }

    private IEnumerator OfflineWaitForEquipmentThenHideRoutine()
    {
        yield return null;
        yield return null;

        float elapsed = 0f;
        while (elapsed < 3.0f)
        {
            if (TruckGearSpawner.Instance != null && TruckGearSpawner.Instance.HasSpawnedLoadout)
            {
                break;
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (LoadingScreenManager.IsLoadingVisible)
        {
            LoadingScreenManager.Instance.SetProgress(1f, "Equipment manifest ready.");
            yield return new WaitForSecondsRealtime(0.18f);
            FinishLoadingIntermission("Equipment manifest ready.");
        }
    }

    private void SetConnectionAddress(string ip, ushort port)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(ip, port);
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
