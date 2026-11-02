using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
            }
            OnPartyStateChanged?.Invoke();
        };

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        OnPartyStateChanged?.Invoke();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        OnPartyStateChanged?.Invoke();
    }

    private void OnSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName == ProgressionAndLoadoutManager.GAME_MAP_SCENE_NAME)
        {
            // Find the PlayerSpawnPoint component
            var spawnPoint = UnityEngine.Object.FindAnyObjectByType<PlayerSpawnPoint>();
            Vector3 startPos = spawnPoint != null ? spawnPoint.transform.position : Vector3.zero;

            int index = 0;
            foreach (var clientId in clientsCompleted)
            {
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    if (client.PlayerObject != null)
                    {
                        Vector3 offset = new Vector3((index % 2) * 0.08f, (index / 2) * 0.08f, 0f);
                        client.PlayerObject.transform.position = startPos + offset;
                        var pc = client.PlayerObject.GetComponent<PlayerController>();
                        if (pc != null) pc.RestoreSanity(100f);
                        index++;
                    }
                }
            }
        }
        else if (sceneName == ProgressionAndLoadoutManager.LOBBY_SCENE_NAME)
        {
            int index = 0;
            foreach (var clientId in clientsCompleted)
            {
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    if (client.PlayerObject != null)
                    {
                        Vector3 spawnPos = new Vector3((index % 2 == 0 ? -0.35f : 0.35f), -0.35f + (index * 0.2f), 0f);
                        client.PlayerObject.transform.position = spawnPos;
                        var pc = client.PlayerObject.GetComponent<PlayerController>();
                        if (pc != null) pc.RestoreSanity(100f);
                        index++;
                    }
                }
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
        bool started = NetworkManager.Singleton != null && NetworkManager.Singleton.StartClient();
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
        NetworkManager.Singleton.StartClient();
    }

    public void DisconnectAndReturnToMenu()
    {
        StopLanResponder();
        CurrentPartyCode = string.Empty;
        IsMultiplayerPartyOpen = false;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        SceneManager.LoadScene(ProgressionAndLoadoutManager.MAIN_MENU_SCENE_NAME);
    }

    public void ReturnToLobbyFromMatch()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            LoadLobbyScene();
        }
        else
        {
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
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(ProgressionAndLoadoutManager.LOBBY_SCENE_NAME, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(ProgressionAndLoadoutManager.LOBBY_SCENE_NAME);
        }
    }

    public void LoadGameScene()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            string targetScene = ProgressionAndLoadoutManager.Instance != null
                ? ProgressionAndLoadoutManager.Instance.GetSelectedSceneName()
                : ProgressionAndLoadoutManager.GAME_MAP_SCENE_NAME;

            // NGO synchronized scene loading across all connected clients
            NetworkManager.Singleton.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
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
}
