using System.Collections;
using System;
using System.Collections.Generic;
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

    [Header("Player Spawn Variation")]
    [Tooltip("Distance (meters) between player spawn slots in the 3x3 grid.")]
    [SerializeField]
    [Min(0.1f)]
    private float _spawnGridSpacing = 1.35f;

    [Tooltip("Extra per-player jitter within each grid slot so spawns aren't perfectly aligned.")]
    [SerializeField]
    [Min(0f)]
    private float _spawnJitterRadius = 0.15f;

    [Tooltip("If true, try a few nearby slots when the preferred slot is blocked.")]
    [SerializeField]
    private bool _spawnTryFindUnblockedSlot = true;

    // 3x3 offsets in a "spiral-ish" order: center first, then around it
    private static readonly Vector2Int[] SpawnGridOffsets =
    {
        new(0, 0),
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
        new(1, 1),
        new(-1, 1),
        new(1, -1),
        new(-1, -1),
    };

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

        // Ensure Lobby UI is removed once we enter gameplay
        // If scenes are loaded globally/additively, the lobby canvas can otherwise persist
        if (loadedWorld)
            DestroyLobbyUiIfPresent();

        if (loadedLobby)
            PhotoRollSession.ResetForLobby();

        if (loadedWorld && _networkManager.IsServerStarted)
            PhotoRollSession.EnsureServerMatchId();

        if (!loadedWorld)
            return;

        // Spawning is server-authoritative
        if (!_networkManager.IsServerStarted || _gameplayPlayerPrefab == null)
            return;

        // Deterministic iteration so spawn slot assignment is stable
        List<NetworkConnection> conns = new(_networkManager.ServerManager.Clients.Values);
        conns.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));

        int nextSpawnSlot = 0;
        foreach (NetworkConnection conn in conns)
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
            Vector3 basePos = prefab.transform.position;
            Quaternion rot = prefab.transform.rotation;
            Vector3 pos = GetVariedSpawnPosition(basePos, rot, conn.ClientId, nextSpawnSlot++, prefab);
            NetworkObject instance = _networkManager.GetPooledInstantiated(prefab, pos, rot, true);
            _networkManager.ServerManager.Spawn(instance, conn);
            _networkManager.SceneManager.AddOwnerToDefaultScene(instance);
        }
    }

    private Vector3 GetVariedSpawnPosition(Vector3 basePos, Quaternion baseRot, int clientId, int preferredSlot, NetworkObject prefab)
    {
        Vector3 right = baseRot * Vector3.right;
        Vector3 forward = baseRot * Vector3.forward;

        int slotCount = SpawnGridOffsets.Length;
        int startSlot = Mod(preferredSlot, slotCount);

        for (int attempt = 0; attempt < slotCount; attempt++)
        {
            if (attempt > 0 && !_spawnTryFindUnblockedSlot)
                break;

            int slot = Mod(startSlot + attempt, slotCount);
            Vector2Int o = SpawnGridOffsets[slot];

            Vector3 offset = (right * (o.x * _spawnGridSpacing)) + (forward * (o.y * _spawnGridSpacing));
            Vector3 jitter = GetDeterministicJitter(clientId, slot) * _spawnJitterRadius;

            Vector3 candidate = basePos + offset + jitter;
            if (!_spawnTryFindUnblockedSlot || IsSpawnCandidateClear(candidate, prefab))
                return candidate;
        }

        return basePos + (right * _spawnGridSpacing) + GetDeterministicJitter(clientId, startSlot) * _spawnJitterRadius;
    }

    private static Vector3 GetDeterministicJitter(int clientId, int salt)
    {
        unchecked
        {
            uint x = (uint)(clientId * 73856093) ^ (uint)(salt * 19349663) ^ 0x9E3779B9u;
            x ^= x >> 16;
            x *= 2246822519u;
            x ^= x >> 13;
            x *= 3266489917u;
            x ^= x >> 16;

            float fx = ((x & 0xFFFFu) / 65535f) * 2f - 1f;
            float fz = (((x >> 16) & 0xFFFFu) / 65535f) * 2f - 1f;

            Vector3 v = new(fx, 0f, fz);
            float mag = v.magnitude;
            if (mag > 1e-5f)
                v /= mag;
            return v;
        }
    }

    private bool IsSpawnCandidateClear(Vector3 position, NetworkObject prefab)
    {
        float radius = 0.5f;
        float height = 2f;
        Vector3 center = Vector3.zero;

        if (prefab != null && prefab.TryGetComponent(out CharacterController cc))
        {
            radius = Mathf.Max(0.05f, cc.radius);
            height = Mathf.Max(radius * 2f + 0.01f, cc.height);
            center = cc.center;
        }

        Vector3 up = Vector3.up;
        float half = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 worldCenter = position + center;
        Vector3 p1 = worldCenter + up * half;
        Vector3 p2 = worldCenter - up * half;

        const QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore;
        bool blocked = Physics.CheckCapsule(p1, p2, radius, ~0, triggers);
        return !blocked;
    }

    private static int Mod(int value, int m)
    {
        int r = value % m;
        return r < 0 ? r + m : r;
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

    private static void DestroyLobbyUiIfPresent()
    {
        GameObject canvas = GameObject.Find("LobbyCanvas");
        if (canvas != null)
            Destroy(canvas);

        LobbyUI ui = FindAnyObjectByType<LobbyUI>();
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
