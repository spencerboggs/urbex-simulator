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

/// <summary>
/// Host/client session lifecycle, transport setup, lobby map sync, and gameplay player spawning.
/// </summary>
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

    /// <summary>True after StartHost until the lobby scene load coroutine runs.</summary>
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

    /// <summary>3x3 spawn slot offsets around the prefab origin (center first).</summary>
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

    /// <summary>Resolves NetworkManager, transport, player prefab, and default map.</summary>
    private void Awake()
    {
        // Wire NetworkManager and gameplay prefab before any connection starts.
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

        EnsureDefaultMapSelection();
    }

    /// <summary>Subscribes to FishNet client, scene, server, and map broadcast events.</summary>
    private void OnEnable()
    {
        if (_networkManager == null)
            return;

        _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        _networkManager.SceneManager.OnLoadEnd += OnFishNetSceneLoadEnd;

        _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        _networkManager.ServerManager.RegisterBroadcast<RequestMapSelectionBroadcast>(OnServerReceivedMapRequest);
        _networkManager.ClientManager.RegisterBroadcast<MapSelectionBroadcast>(OnClientReceivedMapSelection);
    }

    /// <summary>Unsubscribes from FishNet events registered in OnEnable.</summary>
    private void OnDisable()
    {
        if (_networkManager == null)
            return;

        _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        _networkManager.SceneManager.OnLoadEnd -= OnFishNetSceneLoadEnd;

        _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        _networkManager.ServerManager.UnregisterBroadcast<RequestMapSelectionBroadcast>(OnServerReceivedMapRequest);
        _networkManager.ClientManager.UnregisterBroadcast<MapSelectionBroadcast>(OnClientReceivedMapSelection);
    }

    /// <summary>Host-authoritative gameplay scene name from MapCatalog.</summary>
    private string _selectedMapSceneName = string.Empty;

    /// <summary>Server-authoritative selected gameplay scene name (MapCatalog entry).</summary>
    public string SelectedMapSceneName => _selectedMapSceneName;

    /// <summary>Raised when the selected map changes on host or clients.</summary>
    public event Action<string> OnSelectedMapChanged;

    /// <summary>
    /// Last join target (Steam ID for FishySteamworks, or host/IP:port for IP transports).
    /// </summary>
    public string DefaultJoinTarget { get; set; } = string.Empty;

    /// <summary>Applies <see cref="_maxPlayers"/> to the active transport.</summary>
    public void ApplyMaxPlayersToTransport()
    {
        if (_networkManager == null)
            return;
        _networkManager.TransportManager.Transport.SetMaximumClients(_maxPlayers);
    }

    /// <summary>Sets the transport client address and updates <see cref="DefaultJoinTarget"/>.</summary>
    public void SetJoinTarget(string joinTarget)
    {
        if (_networkManager == null || string.IsNullOrWhiteSpace(joinTarget))
            return;
        _networkManager.TransportManager.Transport.SetClientAddress(joinTarget.Trim());
        DefaultJoinTarget = joinTarget.Trim();
    }

    /// <summary>Starts host and optionally loads the lobby when the local client connects.</summary>
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

        TryLogLocalSteamId();
    }

    /// <summary>Starts the client using join field text or <see cref="DefaultJoinTarget"/>.</summary>
    public void StartClient(string joinFieldText)
    {
        if (_networkManager == null)
            return;

        ApplyMaxPlayersToTransport();

        // Parse host:port for IP transports; Steam uses steamId64 as the address.
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

    /// <summary>Starts server only (no automatic scene load).</summary>
    public void StartServerOnly()
    {
        if (_networkManager == null)
            return;

        ApplyMaxPlayersToTransport();

        if (!_networkManager.ServerManager.Started)
            _networkManager.ServerManager.StartConnection();
    }

    /// <summary>Stops networking and loads the main menu offline.</summary>
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

    /// <summary>Host-only: loads the selected gameplay map (with catalog fallbacks).</summary>
    public void StartMatchFromLobby()
    {
        if (_networkManager == null || !_networkManager.IsHostStarted)
            return;

        string sceneName = ResolveStartMatchSceneName();
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[Net] StartMatchFromLobby: no map selected and MapCatalog is empty. Aborting.");
            return;
        }

        NetworkSceneFlow.LoadMap(_networkManager, sceneName, _gameplayPlayerPrefab);
    }

    /// <summary>Returns selected map, first catalog entry, or DefaultMap fallback.</summary>
    private string ResolveStartMatchSceneName()
    {
        if (!string.IsNullOrEmpty(_selectedMapSceneName))
            return _selectedMapSceneName;

        MapCatalog catalog = MapCatalog.Load();
        if (catalog != null && catalog.Count > 0 && catalog.TryGetByIndex(0, out MapCatalog.MapEntry entry))
            return entry.sceneName;

        return NetworkSceneFlow.DefaultMap;
    }

    /// <summary>Host-validated map change; non-host clients broadcast a request to the server.</summary>
    public void RequestMapChange(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || _networkManager == null)
            return;

        if (_networkManager.IsServerStarted)
        {
            if (!IsValidMapForSelection(sceneName))
            {
                Debug.LogWarning($"[Net] RequestMapChange: '{sceneName}' is not in the MapCatalog. Ignoring.");
                return;
            }

            ApplyMapSelectionLocally(sceneName);
            BroadcastMapSelectionToAll(sceneName);
            return;
        }

        if (_networkManager.ClientManager.Started)
        {
            _networkManager.ClientManager.Broadcast(new RequestMapSelectionBroadcast { SceneName = sceneName });
        }
    }

    /// <summary>Sets the first MapCatalog entry when no map was chosen yet.</summary>
    private void EnsureDefaultMapSelection()
    {
        if (!string.IsNullOrEmpty(_selectedMapSceneName))
            return;

        MapCatalog catalog = MapCatalog.Load();
        if (catalog == null || catalog.Count == 0)
            return;

        if (catalog.TryGetByIndex(0, out MapCatalog.MapEntry entry))
            _selectedMapSceneName = entry.sceneName;
    }

    /// <summary>True when sceneName exists in MapCatalog or matches the legacy default.</summary>
    private bool IsValidMapForSelection(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            return false;

        MapCatalog catalog = MapCatalog.Load();
        if (catalog == null || catalog.Count == 0)
        {
            return sceneName == NetworkSceneFlow.DefaultMap;
        }

        return catalog.TryGetBySceneName(sceneName, out _, out _);
    }

    /// <summary>Updates local selection and raises OnSelectedMapChanged when changed.</summary>
    private void ApplyMapSelectionLocally(string sceneName)
    {
        if (_selectedMapSceneName == sceneName)
            return;

        _selectedMapSceneName = sceneName;
        OnSelectedMapChanged?.Invoke(_selectedMapSceneName);
    }

    /// <summary>Server broadcast of the current lobby map to all clients.</summary>
    private void BroadcastMapSelectionToAll(string sceneName)
    {
        if (_networkManager == null || !_networkManager.IsServerStarted)
            return;

        _networkManager.ServerManager.Broadcast(new MapSelectionBroadcast { SceneName = sceneName });
    }

    /// <summary>Validates host-only map change requests from the local client connection.</summary>
    private void OnServerReceivedMapRequest(NetworkConnection sender, RequestMapSelectionBroadcast bc, Channel channel)
    {
        if (sender == null)
            return;

        if (!sender.IsLocalClient)
        {
            Debug.LogWarning($"[Net] Ignoring map change request from non-host connection {sender.ClientId}.");
            return;
        }

        if (!IsValidMapForSelection(bc.SceneName))
        {
            Debug.LogWarning($"[Net] Host requested unknown map '{bc.SceneName}'. Ignoring.");
            return;
        }

        ApplyMapSelectionLocally(bc.SceneName);
        BroadcastMapSelectionToAll(bc.SceneName);
    }

    /// <summary>Applies map selection broadcast from the server on clients.</summary>
    private void OnClientReceivedMapSelection(MapSelectionBroadcast bc, Channel channel)
    {
        if (string.IsNullOrEmpty(bc.SceneName))
            return;

        ApplyMapSelectionLocally(bc.SceneName);
    }

    /// <summary>Sends the current map selection to clients as they connect.</summary>
    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (conn == null || args.ConnectionState != RemoteConnectionState.Started)
            return;

        if (_networkManager == null || !_networkManager.IsServerStarted)
            return;

        if (string.IsNullOrEmpty(_selectedMapSceneName))
            return;

        _networkManager.ServerManager.Broadcast(conn, new MapSelectionBroadcast { SceneName = _selectedMapSceneName });
    }

    /// <summary>Loads the lobby after host client connect when StartHost requested it.</summary>
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

    /// <summary>Deferred lobby load so server and client are both started.</summary>
    private IEnumerator LoadLobbyWhenHostReady()
    {
        yield return null;
        if (_networkManager != null && _networkManager.IsServerStarted)
            NetworkSceneFlow.LoadLobby(_networkManager);
    }

    /// <summary>Enables P2P on FishySteamworks and clears localhost join target.</summary>
    private void ConfigureSteamTransportIfPresent()
    {
        if (_networkManager == null || _networkManager.TransportManager == null)
            return;

        Transport active = _networkManager.TransportManager.Transport;
        if (active == null)
            return;

        if (!IsSteamTransportActive())
            return;

        TrySetBoolField(active, "_peerToPeer", true);

        if (DefaultJoinTarget.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            DefaultJoinTarget = string.Empty;
    }

    /// <summary>Logs local steamId64 and seeds DefaultJoinTarget when Steam transport is active.</summary>
    private void TryLogLocalSteamId()
    {
        if (!IsSteamTransportActive())
            return;

        if (_networkManager == null || _networkManager.TransportManager == null)
            return;

        Transport t = _networkManager.TransportManager.Transport;
        if (t == null)
            return;

        FieldInfo f = t.GetType().GetField("LocalUserSteamID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null)
            return;

        object v = f.GetValue(t);
        if (v is ulong id && id != 0)
        {
            Debug.Log($"[Steam] Local steamId64: {id} (friends can paste this into Join to connect).");
            DefaultJoinTarget = id.ToString();
        }
    }

    /// <summary>Parses host, optional port, IPv6 brackets, and scheme prefixes for IP transports.</summary>
    private static bool TryParseEndpointForIpTransport(string input, out string host, out ushort? port)
    {
        host = null;
        port = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Strip scheme and path; support [ipv6]:port and host:port.
        string s = input.Trim();

        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
            s = s[(scheme + 3)..];
        int slash = s.IndexOf('/');
        if (slash >= 0)
            s = s[..slash];

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

    /// <summary>Sets transport port via reflection when the transport exposes a setter or field.</summary>
    private static void TrySetTransportPort(Transport transport, ushort port)
    {
        if (transport == null)
            return;

        try
        {
            Type t = transport.GetType();

            // Try common FishNet / Tugboat port setter names first.
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

    /// <summary>Tears down menu UI, resets photo roll, and spawns players after gameplay load.</summary>
    private void OnFishNetSceneLoadEnd(SceneLoadEndEventArgs args)
    {
        if (_networkManager == null)
            return;

        // Classify loaded scenes so we can run lobby vs gameplay hooks.
        bool loadedLobby = false;
        bool loadedGameplay = false;
        for (int i = 0; i < args.LoadedScenes.Length; i++)
        {
            Scene s = args.LoadedScenes[i];
            if (!s.IsValid())
                continue;

            if (s.name == NetworkSceneFlow.Lobby)
                loadedLobby = true;
            else if (s.name != NetworkSceneFlow.MainMenu && NetworkSceneFlow.IsGameplayScene(s.name))
                loadedGameplay = true;
        }

        if (loadedLobby || loadedGameplay)
            DestroyMainMenuUiIfPresent();

        if (loadedGameplay)
            DestroyLobbyUiIfPresent();

        if (loadedLobby)
            PhotoRollSession.ResetForLobby();

        if (loadedGameplay && _networkManager.IsServerStarted)
            PhotoRollSession.EnsureServerMatchId();

        if (!loadedGameplay)
            return;

        if (!_networkManager.IsServerStarted || _gameplayPlayerPrefab == null)
            return;

        // Spawn one gameplay player per connection that does not already own one.
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

    /// <summary>Picks a grid slot with optional jitter and overlap checks for the given client.</summary>
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

    /// <summary>Stable unit XZ jitter from client id and slot index (no random per frame).</summary>
    private static Vector3 GetDeterministicJitter(int clientId, int salt)
    {
        unchecked
        {
            // Hash client and slot into a reproducible direction on the XZ plane.
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

    /// <summary>Capsule overlap test at candidate using the prefab CharacterController size.</summary>
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

    /// <summary>Non-negative modulo for spawn grid indexing.</summary>
    private static int Mod(int value, int m)
    {
        int r = value % m;
        return r < 0 ? r + m : r;
    }

    /// <summary>Removes runtime or authored main menu objects after scene transition.</summary>
    private static void DestroyMainMenuUiIfPresent()
    {
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

    /// <summary>Removes runtime or authored lobby UI when entering gameplay.</summary>
    private static void DestroyLobbyUiIfPresent()
    {
        GameObject canvas = GameObject.Find("LobbyCanvas");
        if (canvas != null)
            Destroy(canvas);

        LobbyUI ui = FindAnyObjectByType<LobbyUI>();
        if (ui != null)
            Destroy(ui.gameObject);
    }

    /// <summary>True when the active transport type name suggests Steam / FishySteamworks.</summary>
    private bool IsSteamTransportActive()
    {
        if (_networkManager == null || _networkManager.TransportManager == null)
            return false;

        Transport t = _networkManager.TransportManager.Transport;
        string fullName = t?.GetType()?.FullName ?? string.Empty;
        return fullName.IndexOf("FishySteamworks", StringComparison.OrdinalIgnoreCase) >= 0
               || fullName.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Sets a bool field on transport via reflection when present.</summary>
    private static void TrySetBoolField(object obj, string fieldName, bool value)
    {
        if (obj == null || string.IsNullOrWhiteSpace(fieldName))
            return;

        FieldInfo f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null || f.FieldType != typeof(bool))
            return;

        f.SetValue(obj, value);
    }

    /// <summary>Selects FishySteamworks as the active transport when attached on this object.</summary>
    private void PreferSteamTransportIfPresent()
    {
        if (_networkManager == null || _networkManager.TransportManager == null)
            return;

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

    /// <summary>Assigns Transport on TransportManager via property or known field names.</summary>
    private static void TrySetActiveTransport(object transportManager, Transport transport)
    {
        if (transportManager == null || transport == null)
            return;

        Type tmType = transportManager.GetType();

        PropertyInfo transportProp = tmType.GetProperty("Transport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (transportProp != null && transportProp.CanWrite && transportProp.PropertyType.IsAssignableFrom(transport.GetType()))
        {
            transportProp.SetValue(transportManager, transport);
            Debug.Log($"[Net] Active transport set to {transport.GetType().Name} via TransportManager.Transport property.");
            return;
        }

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
