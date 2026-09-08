using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkController : MonoBehaviour
{
    public static NetworkController Instance { get; private set; }

    [SerializeField] private string defaultIpAddress = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Wait for NetworkManager to initialize
        StartCoroutine(WaitForNetworkManager());
    }

    private IEnumerator WaitForNetworkManager()
    {
        while (NetworkManager.Singleton == null)
        {
            yield return null;
        }

        NetworkManager.Singleton.OnServerStarted += () =>
        {
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
            }
        };
    }

    private void OnSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName == "GameMapScene")
        {
            // Find the PlayerSpawnPoint component
            var spawnPoint = Object.FindAnyObjectByType<PlayerSpawnPoint>();
            Vector3 startPos = spawnPoint != null ? spawnPoint.transform.position : Vector3.zero;

            foreach (var clientId in clientsCompleted)
            {
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    if (client.PlayerObject != null)
                    {
                        // Move player to the van spawn point
                        client.PlayerObject.transform.position = startPos;
                    }
                }
            }
        }
    }

    public void StartSingleplayer()
    {
        // Singleplayer runs locally using host mode
        SetConnectionAddress("127.0.0.1", defaultPort);
        NetworkManager.Singleton.StartHost();
        LoadGameScene();
    }

    public void HostGame()
    {
        SetConnectionAddress("0.0.0.0", defaultPort); // Listen on all local adapters
        NetworkManager.Singleton.StartHost();
    }

    public void JoinGame(string ipInput)
    {
        string targetIp = string.IsNullOrEmpty(ipInput) ? defaultIpAddress : ipInput;
        SetConnectionAddress(targetIp, defaultPort);
        NetworkManager.Singleton.StartClient();
    }

    public void DisconnectAndReturnToMenu()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        SceneManager.LoadScene("MainMenuScene");
    }

    public void KickPlayer(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer && clientId != NetworkManager.Singleton.LocalClientId)
        {
            NetworkManager.Singleton.DisconnectClient(clientId);
        }
    }

    public void LoadGameScene()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            // NGO synchronized scene loading across all connected clients
            NetworkManager.Singleton.SceneManager.LoadScene("GameMapScene", LoadSceneMode.Single);
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
}