using System.Collections;
using System;
using System.Reflection;
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

        PreferSteamTransportIfPresent();
        ConfigureSteamTransportIfPresent();
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

    // Selected map / level index from the lobby UI
    // Gameplay systems can read this when loading the match
    public int SelectedMapIndex { get; set; }

    // Last join value synced with the main menu join field. With FishySteamworks this is the host's
    // Steam ID (steamId64). For non-Steam transports (local dev) this may be a hostname, IP, or host:port.
    public string DefaultJoinTarget { get; set; } = string.Empty;

    // Apply transport limits so the lobby capacity matches FishNet server settings
    public void ApplyMaxPlayersToTransport()
    {
        if (_networkManager == null)
            return;
        _networkManager.TransportManager.Transport.SetMaximumClients(_maxPlayers);
    }

    // Sets the transport client target and remembers it for the join UI (same format as DefaultJoinTarget)
    public void SetJoinTarget(string joinTarget)
    {
        if (_networkManager == null || string.IsNullOrWhiteSpace(joinTarget))
            return;
        _networkManager.TransportManager.Transport.SetClientAddress(joinTarget.Trim());
        DefaultJoinTarget = joinTarget.Trim();
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

        // Helpful for Steam P2P (host needs an ID to share)
        TryLogLocalSteamId();
    }

    // Starts the FishNet client. Text comes from the join field
    // Host Steam ID (steamId64) for FishySteamworks,
    // or hostname / IP / host:port when a non-Steam transport is active
    public void StartClient(string joinFieldText)
    {
        if (_networkManager == null)
            return;

        ApplyMaxPlayersToTransport();

        string input = string.IsNullOrWhiteSpace(joinFieldText) ? DefaultJoinTarget : joinFieldText;
        input = input?.Trim() ?? string.Empty;

        string clientAddress = input;
        ushort? port = null;

        bool steam = IsSteamTransportActive();
        if (!steam)
        {
            if (!TryParseEndpointForIpTransport(input, out clientAddress, out port))
                clientAddress = input;
        }

        if (string.IsNullOrWhiteSpace(clientAddress))
        {
            if (steam)
            {
                Debug.LogWarning("[Net] Join target is empty. Enter the host's Steam ID (steamId64).");
                return;
            }

            clientAddress = "localhost";
        }

        clientAddress = clientAddress.Trim();
        _networkManager.TransportManager.Transport.SetClientAddress(clientAddress);
        DefaultJoinTarget = port.HasValue ? $"{clientAddress}:{port.Value}" : clientAddress;

        if (port.HasValue)
            TrySetTransportPort(_networkManager.TransportManager.Transport, port.Value);

        if (_networkManager.ClientManager.Started)
            _networkManager.ClientManager.StopConnection();

        Debug.Log($"[Net] Client connecting (target: {DefaultJoinTarget})...");
        _networkManager.ClientManager.StartConnection();
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
        Debug.Log($"[Net] Client connection state: {args.ConnectionState}");
        if (args.ConnectionState != LocalConnectionState.Started)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (IsSteamTransportActive())
                    Debug.LogWarning("[Net] Client connection stopped/failed. If you're using Steam P2P, make sure Steam is running, both accounts can see each other / are allowed, and you're connecting to the host's steamId64.");
                else
                    Debug.LogWarning("[Net] Client connection stopped/failed. For IP-based transports, confirm the host endpoint, port (often 7770 UDP/TCP), firewall, and port forwarding if needed.");
            }
            return;
        }

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

    private void ConfigureSteamTransportIfPresent()
    {
        if (_networkManager == null || _networkManager.TransportManager == null)
            return;

        Transport active = _networkManager.TransportManager.Transport;
        if (active == null)
            return;

        // FishySteamworks: prefer P2P via Steam relay so clients do not rely on manual port forwarding
        if (!IsSteamTransportActive())
            return;

        TrySetBoolField(active, "_peerToPeer", true);

        // Legacy sessions may still have an IP-style placeholder; Steam joins use steamId64 only
        if (DefaultJoinTarget.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            DefaultJoinTarget = string.Empty;
    }

    private void TryLogLocalSteamId()
    {
        if (!IsSteamTransportActive())
            return;

        if (_networkManager == null || _networkManager.TransportManager == null)
            return;

        Transport t = _networkManager.TransportManager.Transport;
        if (t == null)
            return;

        // FishySteamworks has a public non-serialized field LocalUserSteamID
        FieldInfo f = t.GetType().GetField("LocalUserSteamID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null)
            return;

        object v = f.GetValue(t);
        if (v is ulong id && id != 0)
        {
            Debug.Log($"[Steam] Local steamId64: {id} (friends can paste this into Join to connect).");
            // Prefill the join field with this host's ID after StartHost
            DefaultJoinTarget = id.ToString();
        }
    }

    // host:port parsing for Tugboat / IP transports only (FishySteamworks uses a single steamId64 string)
    private static bool TryParseEndpointForIpTransport(string input, out string host, out ushort? port)
    {
        host = null;
        port = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string s = input.Trim();

        // Allow users to paste things like "http://1.2.3.4:7770/" or "1.2.3.4:7770"
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
            s = s[(scheme + 3)..];
        int slash = s.IndexOf('/');
        if (slash >= 0)
            s = s[..slash];

        // IPv6 may be entered as "[::1]:7770"
        if (s.StartsWith("[", StringComparison.Ordinal))
        {
            int end = s.IndexOf(']');
            if (end <= 0)
                return false;
            host = s.Substring(1, end - 1);
            if (end + 1 < s.Length && s[end + 1] == ':' && ushort.TryParse(s[(end + 2)..], out ushort p6))
                port = p6;
            return true;
        }

        int lastColon = s.LastIndexOf(':');
        if (lastColon > 0 && lastColon < s.Length - 1)
        {
            string left = s[..lastColon];
            string right = s[(lastColon + 1)..];
            if (ushort.TryParse(right, out ushort p))
            {
                host = left;
                port = p;
                return true;
            }
        }

        host = s;
        return true;
    }

    private static void TrySetTransportPort(Transport transport, ushort port)
    {
        if (transport == null)
            return;

        try
        {
            Type t = transport.GetType();

            // Common method names across transports
            foreach (string methodName in new[] { "SetPort", "SetClientPort", "SetServerPort" })
            {
                MethodInfo m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null)
                    continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 1 && (ps[0].ParameterType == typeof(ushort) || ps[0].ParameterType == typeof(int)))
                {
                    object arg = ps[0].ParameterType == typeof(int) ? (object)(int)port : port;
                    m.Invoke(transport, new[] { arg });
                    Debug.Log($"[Net] Transport port set via {t.Name}.{methodName}({port}).");
                    return;
                }
            }

            // Tugboat stores it as a serialized field "_port"
            FieldInfo f = t.GetField("_port", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null && (f.FieldType == typeof(ushort) || f.FieldType == typeof(int)))
            {
                object arg = f.FieldType == typeof(int) ? (object)(int)port : port;
                f.SetValue(transport, arg);
                Debug.Log($"[Net] Transport port set via {t.Name}._port = {port}.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Net] Could not set transport port to {port}. {e.GetType().Name}: {e.Message}");
        }
    }

    private void OnFishNetSceneLoadEnd(SceneLoadEndEventArgs args)
    {
        if (_networkManager == null)
            return;

        bool loadedLobby = false;
        bool loadedWorld = false;
        for (int i = 0; i < args.LoadedScenes.Length; i++)
        {
            Scene s = args.LoadedScenes[i];
            if (!s.IsValid())
                continue;

            if (s.name == NetworkSceneFlow.Lobby)
                loadedLobby = true;
            else if (s.name == NetworkSceneFlow.World)
                loadedWorld = true;
        }

        // FishNet global scene loading doesn't necessarily unload any locally-loaded "offline" scene UI
        // Ensure MainMenu UI is removed once we transition into Lobby/World on both host and clients
        if (loadedLobby || loadedWorld)
            DestroyMainMenuUiIfPresent();

        if (!loadedWorld)
            return;

        // Spawning is server-authoritative
        if (!_networkManager.IsServerStarted || _gameplayPlayerPrefab == null)
            return;

        foreach (NetworkConnection conn in _networkManager.ServerManager.Clients.Values)
        {
            if (!conn.IsActive || !conn.IsAuthenticated)
                continue;

            bool hasSpawnedOwned = false;
            foreach (NetworkObject nob in conn.Objects)
            {
                if (nob == null || !nob.IsSpawned)
                    continue;

                // Only treat an owned "player" as satisfied
                // Connections may own other objects (especially host)
                if (nob.TryGetComponent(out PlayerLocalControls _))
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

    private static void DestroyMainMenuUiIfPresent()
    {
        // Generated UI uses these names; authored UI might differ, so we also remove known controller scripts
        GameObject canvas = GameObject.Find("MainMenuCanvas");
        if (canvas != null)
            Destroy(canvas);

        GameObject menuCamera = GameObject.Find("MenuCamera");
        if (menuCamera != null)
            Destroy(menuCamera);

        MainMenuUI ui = FindAnyObjectByType<MainMenuUI>();
        if (ui != null)
            Destroy(ui.gameObject);
    }

    private bool IsSteamTransportActive()
    {
        if (_networkManager == null || _networkManager.TransportManager == null)
            return false;

        Transport t = _networkManager.TransportManager.Transport;
        string fullName = t?.GetType()?.FullName ?? string.Empty;
        return fullName.IndexOf("FishySteamworks", StringComparison.OrdinalIgnoreCase) >= 0
               || fullName.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void TrySetBoolField(object obj, string fieldName, bool value)
    {
        if (obj == null || string.IsNullOrWhiteSpace(fieldName))
            return;

        FieldInfo f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null || f.FieldType != typeof(bool))
            return;

        f.SetValue(obj, value);
    }

    private void PreferSteamTransportIfPresent()
    {
        if (_networkManager == null || _networkManager.TransportManager == null)
            return;

        // FishySteamworks is added as a Transport component on the same GameObject as NetworkManager
        // We use reflection to stay resilient if the transport isn't installed in some dev environments
        Transport[] transports = GetComponents<Transport>();
        if (transports == null || transports.Length == 0)
            return;

        Transport steamCandidate = null;
        for (int i = 0; i < transports.Length; i++)
        {
            Transport t = transports[i];
            string fullName = t?.GetType()?.FullName ?? string.Empty;
            if (fullName.IndexOf("FishySteamworks", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                steamCandidate = t;
                break;
            }
        }

        if (steamCandidate == null)
            return;

        TrySetActiveTransport(_networkManager.TransportManager, steamCandidate);
    }

    private static void TrySetActiveTransport(object transportManager, Transport transport)
    {
        if (transportManager == null || transport == null)
            return;

        Type tmType = transportManager.GetType();

        // Try property first
        PropertyInfo transportProp = tmType.GetProperty("Transport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (transportProp != null && transportProp.CanWrite && transportProp.PropertyType.IsAssignableFrom(transport.GetType()))
        {
            transportProp.SetValue(transportManager, transport);
            Debug.Log($"[Net] Active transport set to {transport.GetType().Name} via TransportManager.Transport property.");
            return;
        }

        // Then try a field
        FieldInfo transportField = tmType.GetField("Transport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                   ?? tmType.GetField("_transport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                   ?? tmType.GetField("transport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (transportField != null && transportField.FieldType.IsAssignableFrom(transport.GetType()))
        {
            transportField.SetValue(transportManager, transport);
            Debug.Log($"[Net] Active transport set to {transport.GetType().Name} via TransportManager field ({transportField.Name}).");
        }
    }
}
