using System.Collections;
using FishNet.Component.Spawning;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

[DisallowMultipleComponent]
public sealed class NetworkSessionController : MonoBehaviour
{
    [SerializeField]
    private NetworkManager _networkManager;

    [SerializeField]
    [Tooltip("Spawned when entering gameplay (assigned to PlayerSpawner before loading the gameplay scene).")]
    private NetworkObject _gameplayPlayerPrefab;

    [SerializeField]
    [Min(1)]
    private int _maxPlayers = 8;

    [SerializeField]
    [Tooltip("If true, StartHost queues a move into the lobby scene once the local client connection is up.")]
    private bool _enterLobbyAfterHost = true;

    private bool _pendingLobbyAfterHost;

    private void Awake()
    {
        if (_networkManager == null)
            _networkManager = GetComponent<NetworkManager>();
        if (_networkManager == null)
            _networkManager = FindAnyObjectByType<NetworkManager>();

        ApplyMaxPlayersToTransport();

        if (_networkManager != null && _gameplayPlayerPrefab != null &&
            _networkManager.TryGetComponent(out PlayerSpawner spawner))
            spawner.SetPlayerPrefab(_gameplayPlayerPrefab);
    }

    private void OnEnable()
    {
        if (_networkManager == null)
            return;

        _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        _networkManager.SceneManager.OnLoadEnd += OnFishNetSceneLoadEnd;
    }

    private void OnDisable()
    {
        if (_networkManager == null)
            return;

        _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        _networkManager.SceneManager.OnLoadEnd -= OnFishNetSceneLoadEnd;
    }

    // Selected map / level index from the lobby UI; gameplay systems can read this when loading the match
    public int SelectedMapIndex { get; set; }

    // Default WAN/LAN address shown in the join field (e.g. localhost for testing)
    public string DefaultJoinAddress { get; set; } = "localhost";

    // Apply transport limits so the lobby capacity matches FishNet server settings
    public void ApplyMaxPlayersToTransport()
    {
        if (_networkManager == null)
            return;
        _networkManager.TransportManager.Transport.SetMaximumClients(_maxPlayers);
    }

    // Sets where the client socket will connect
    public void SetRemoteServerAddress(string address)
    {
        if (_networkManager == null || string.IsNullOrWhiteSpace(address))
            return;
        _networkManager.TransportManager.Transport.SetClientAddress(address.Trim());
        DefaultJoinAddress = address.Trim();
    }

    // Starts server and local client (host), then loads the lobby scene when connections are ready
    public void StartHost()
    {
        if (_networkManager == null)
            return;

        ApplyMaxPlayersToTransport();

        if (!_networkManager.ServerManager.Started)
            _networkManager.ServerManager.StartConnection();

        if (!_networkManager.ClientManager.Started)
        {
            _pendingLobbyAfterHost = _enterLobbyAfterHost;
            _networkManager.ClientManager.StartConnection();
        }
        else if (_enterLobbyAfterHost)
        {
            StartCoroutine(LoadLobbyWhenHostReady());
        }
    }

    // Connects a client to the given address (host's WAN IP, LAN IP, or localhost)
    public void StartClient(string remoteAddress)
    {
        if (_networkManager == null)
            return;

        ApplyMaxPlayersToTransport();

        if (string.IsNullOrWhiteSpace(remoteAddress))
            remoteAddress = DefaultJoinAddress ?? "localhost";

        _networkManager.ClientManager.StartConnection(remoteAddress.Trim());
    }

    // Starts only the server (headless / tooling). Does not load scenes automatically
    public void StartServerOnly()
    {
        if (_networkManager == null)
            return;

        ApplyMaxPlayersToTransport();

        if (!_networkManager.ServerManager.Started)
            _networkManager.ServerManager.StartConnection();
    }

    // Stops networking and returns to the main menu scene (offline)
    public void DisconnectAndReturnToMainMenu()
    {
        if (_networkManager == null)
            return;

        _pendingLobbyAfterHost = false;

        if (_networkManager.ServerManager.Started)
            _networkManager.ServerManager.StopConnection(true);
        if (_networkManager.ClientManager.Started)
            _networkManager.ClientManager.StopConnection();

        if (UnitySceneManager.GetActiveScene().name != NetworkSceneFlow.MainMenu)
            UnitySceneManager.LoadScene(NetworkSceneFlow.MainMenu);
    }

    // Server-authoritative transition from lobby to gameplay (call from host UI only)
    public void StartMatchFromLobby()
    {
        if (_networkManager == null || !_networkManager.IsHostStarted)
            return;

        NetworkSceneFlow.LoadWorld(_networkManager, _gameplayPlayerPrefab);
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState != LocalConnectionState.Started)
            return;

        if (!_pendingLobbyAfterHost)
            return;

        _pendingLobbyAfterHost = false;
        StartCoroutine(LoadLobbyWhenHostReady());
    }

    private IEnumerator LoadLobbyWhenHostReady()
    {
        yield return null;
        if (_networkManager != null && _networkManager.IsServerStarted)
            NetworkSceneFlow.LoadLobby(_networkManager);
    }

    private void OnFishNetSceneLoadEnd(SceneLoadEndEventArgs args)
    {
        if (_networkManager == null || !_networkManager.IsServerStarted || _gameplayPlayerPrefab == null)
            return;

        bool loadedWorld = false;
        for (int i = 0; i < args.LoadedScenes.Length; i++)
        {
            Scene s = args.LoadedScenes[i];
            if (s.IsValid() && s.name == NetworkSceneFlow.World)
            {
                loadedWorld = true;
                break;
            }
        }

        if (!loadedWorld)
            return;

        foreach (NetworkConnection conn in _networkManager.ServerManager.Clients.Values)
        {
            if (!conn.IsActive || !conn.IsAuthenticated)
                continue;

            bool hasSpawnedOwned = false;
            foreach (NetworkObject nob in conn.Objects)
            {
                if (nob != null && nob.IsSpawned)
                {
                    hasSpawnedOwned = true;
                    break;
                }
            }

            if (hasSpawnedOwned)
                continue;

            NetworkObject prefab = _gameplayPlayerPrefab;
            Vector3 pos = prefab.transform.position;
            Quaternion rot = prefab.transform.rotation;
            NetworkObject instance = _networkManager.GetPooledInstantiated(prefab, pos, rot, true);
            _networkManager.ServerManager.Spawn(instance, conn);
            _networkManager.SceneManager.AddOwnerToDefaultScene(instance);
        }
    }
}
