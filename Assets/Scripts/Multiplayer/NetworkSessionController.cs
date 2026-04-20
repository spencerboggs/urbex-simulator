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

        // Helpful for Steam P2P: host needs an ID to share.
        TryLogLocalSteamId();
    }

    // Connects a client to the given address (host's WAN IP, LAN IP, or localhost)
    public void StartClient(string remoteAddress)
    {
        if (_networkManager == null)
            return;

        ApplyMaxPlayersToTransport();

        string input = string.IsNullOrWhiteSpace(remoteAddress) ? (DefaultJoinAddress ?? "localhost") : remoteAddress;
        string host = input?.Trim();
        ushort? port = null;

        // For IP-based transports we allow "host:port"; for FishySteamworks the "address" is a steamId64 string.
        if (!IsSteamTransportActive())
        {
            if (!TryParseAddressAndPort(input, out host, out port))
                host = input.Trim();
        }

        if (string.IsNullOrWhiteSpace(host))
            host = "localhost";

        host = host.Trim();
        _networkManager.TransportManager.Transport.SetClientAddress(host);
        DefaultJoinAddress = port.HasValue ? $"{host}:{port.Value}" : host;

        if (port.HasValue)
            TrySetTransportPort(_networkManager.TransportManager.Transport, port.Value);

        if (_networkManager.ClientManager.Started)
            _networkManager.ClientManager.StopConnection();

        Debug.Log($"[Net] Client connecting to {DefaultJoinAddress}...");
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
                    Debug.LogWarning("[Net] Client connection stopped/failed. If you're using Steam P2P, make sure Steam is running, both accounts can see each other / are allowed, and you're connecting to a valid steamId64 (host).");
                else
                    Debug.LogWarning("[Net] Client connection stopped/failed. If you're using a WAN IP, make sure the host forwarded port 7770 (UDP/TCP) and allowed it through firewall.");
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

        // If the active transport is FishySteamworks, default it to Peer-to-Peer (Steam Relay)
        // so users never need port-forwarding.
        if (!IsSteamTransportActive())
            return;

        TrySetBoolField(active, "_peerToPeer", true);

        // For Steam transports the "join address" should be the host's steamId64.
        if (DefaultJoinAddress == "localhost")
            DefaultJoinAddress = string.Empty;
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

        // FishySteamworks has a public non-serialized field LocalUserSteamID.
        FieldInfo f = t.GetType().GetField("LocalUserSteamID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null)
            return;

        object v = f.GetValue(t);
        if (v is ulong id && id != 0)
        {
            Debug.Log($"[Steam] Local steamId64: {id} (share this with friends to join).");
            // Convenience: set the join field to something meaningful when hosting.
            DefaultJoinAddress = id.ToString();
        }
    }

    private static bool TryParseAddressAndPort(string input, out string host, out ushort? port)
    {
        host = null;
        port = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string s = input.Trim();

        // Allow users to paste things like "http://1.2.3.4:7770/" or "1.2.3.4:7770".
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

            // Common method names across transports.
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

            // Tugboat stores it as a serialized field "_port".
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

        // FishySteamworks is added as a Transport component on the same GameObject as NetworkManager.
        // We use reflection to stay resilient if the transport isn't installed in some dev environments.
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

        // Try property first.
        PropertyInfo transportProp = tmType.GetProperty("Transport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (transportProp != null && transportProp.CanWrite && transportProp.PropertyType.IsAssignableFrom(transport.GetType()))
        {
            transportProp.SetValue(transportManager, transport);
            Debug.Log($"[Net] Active transport set to {transport.GetType().Name} via TransportManager.Transport property.");
            return;
        }

        // Then try a field.
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
