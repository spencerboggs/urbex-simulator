using FishNet.Managing;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Lobby staging UI: player count, map selection, host start match, and leave session.
/// Map data comes from <see cref="MapCatalog"/>; selection syncs via <see cref="NetworkSessionController"/>.
/// </summary>
public sealed class LobbyUI : MonoBehaviour
{
    [SerializeField]
    private NetworkSessionController _session;

    [SerializeField]
    private Text _playerCountLabel;

    [SerializeField]
    private Text _mapLabel;

    [SerializeField]
    private Button _mapNextButton;

    [SerializeField]
    private Button _startMatchButton;

    [SerializeField]
    private Button _leaveButton;

    [SerializeField]
    [Tooltip("If no labels are assigned, a default Canvas is generated at runtime.")]
    private bool _generateDefaultUiIfEmpty = true;

    /// <summary>Loaded MapCatalog for display names and cycling.</summary>
    private MapCatalog _catalog;

    /// <summary>True after button listeners are registered once.</summary>
    private bool _wired;

    /// <summary>Suppresses accidental Start Match clicks right after OnEnable.</summary>
    private float _ignoreStartMatchClicksUntil;

    /// <summary>True while OnSelectedMapChanged is subscribed on the session.</summary>
    private bool _subscribedToSession;

    /// <summary>Builds default UI, loads catalog, and wires button listeners.</summary>
    private void Awake()
    {
        // Ensure menu scenes have camera, EventSystem, and optional generated canvas.
        MultiplayerUiRuntimeBuilder.EnsureSceneCamera();
        MultiplayerUiRuntimeBuilder.EnsureEventSystem();

        if (_generateDefaultUiIfEmpty && _playerCountLabel == null)
            MultiplayerUiRuntimeBuilder.BuildLobby(this);

        if (_session == null)
            _session = FindAnyObjectByType<NetworkSessionController>();

        _catalog = MapCatalog.Load();
        if (_catalog == null || _catalog.Count == 0)
        {
            Debug.LogWarning("[LobbyUI] MapCatalog is missing or empty. Add scenes to Assets/Scenes/Gameplay/ and run Tools/Urbex/Refresh Map Catalog.");
        }

        WireListenersOnce();
    }

    /// <summary>Subscribes to map changes and clears UI focus after enable.</summary>
    private void OnEnable()
    {
        SubscribeToSession();
        RefreshMapLabel();
        _ignoreStartMatchClicksUntil = Time.unscaledTime + 0.25f;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Unsubscribes from session map events.</summary>
    private void OnDisable()
    {
        UnsubscribeFromSession();
    }

    /// <summary>Refreshes player count and host-only control interactability each frame.</summary>
    private void Update()
    {
        UpdatePlayerCount();
        UpdateHostButtons();
    }

    /// <summary>Wires runtime-generated UI references from <see cref="MultiplayerUiRuntimeBuilder"/>.</summary>
    public void ApplyRuntimeReferences(Text playerCount, Text mapLabel, Button mapNext, Button startMatch, Button leave)
    {
        _playerCountLabel = playerCount;
        _mapLabel = mapLabel;
        _mapNextButton = mapNext;
        _startMatchButton = startMatch;
        _leaveButton = leave;
    }

    /// <summary>Registers onClick handlers for lobby buttons (idempotent).</summary>
    private void WireListenersOnce()
    {
        if (_wired)
            return;
        _wired = true;

        if (_startMatchButton != null)
            _startMatchButton.onClick.AddListener(OnStartMatchClicked);

        if (_leaveButton != null)
            _leaveButton.onClick.AddListener(OnLeaveClicked);

        if (_mapNextButton != null)
            _mapNextButton.onClick.AddListener(OnMapNextClicked);
    }

    /// <summary>Subscribes to NetworkSessionController map selection updates.</summary>
    private void SubscribeToSession()
    {
        if (_session == null)
            _session = FindAnyObjectByType<NetworkSessionController>();
        if (_session == null || _subscribedToSession)
            return;

        _session.OnSelectedMapChanged += OnSelectedMapChanged;
        _subscribedToSession = true;
    }

    /// <summary>Unsubscribes from session map selection updates.</summary>
    private void UnsubscribeFromSession()
    {
        if (_session == null || !_subscribedToSession)
            return;

        _session.OnSelectedMapChanged -= OnSelectedMapChanged;
        _subscribedToSession = false;
    }

    /// <summary>Refreshes the map label when the host changes selection.</summary>
    private void OnSelectedMapChanged(string sceneName)
    {
        RefreshMapLabel();
    }

    /// <summary>Sets map label text from catalog display name or scene name.</summary>
    private void RefreshMapLabel()
    {
        if (_mapLabel == null)
            return;

        if (_catalog == null || _catalog.Count == 0)
        {
            _mapLabel.text = "Map: (no maps available)";
            return;
        }

        string sceneName = _session != null ? _session.SelectedMapSceneName : string.Empty;

        if (string.IsNullOrEmpty(sceneName) && _catalog.TryGetByIndex(0, out MapCatalog.MapEntry first))
            sceneName = first.sceneName;

        if (_catalog.TryGetBySceneName(sceneName, out MapCatalog.MapEntry entry, out _))
            _mapLabel.text = $"Map: {entry.displayName}";
        else
            _mapLabel.text = $"Map: {sceneName}";
    }

    /// <summary>Host cycles to the next map in MapCatalog and requests sync.</summary>
    private void OnMapNextClicked()
    {
        if (_session == null || _catalog == null || _catalog.Count == 0)
            return;

        if (!IsHost())
            return;

        // Advance circularly through catalog entries by scene name index.
        string currentScene = _session.SelectedMapSceneName;
        int currentIndex = -1;
        if (!string.IsNullOrEmpty(currentScene))
            _catalog.TryGetBySceneName(currentScene, out _, out currentIndex);

        int nextIndex = (currentIndex + 1) % _catalog.Count;
        if (_catalog.TryGetByIndex(nextIndex, out MapCatalog.MapEntry nextEntry))
            _session.RequestMapChange(nextEntry.sceneName);
    }

    /// <summary>Updates the player count label from server client list size.</summary>
    private void UpdatePlayerCount()
    {
        if (_playerCountLabel == null)
            return;

        NetworkManager nm = FindAnyObjectByType<NetworkManager>();
        if (nm == null || !nm.IsServerStarted)
        {
            _playerCountLabel.text = "Players: --";
            return;
        }

        int count = nm.ServerManager.Clients.Count;
        _playerCountLabel.text = $"Players: {count} / 8";
    }

    /// <summary>Enables Start Match and Next Map only for the host.</summary>
    private void UpdateHostButtons()
    {
        bool host = IsHost();

        if (_startMatchButton != null)
            _startMatchButton.interactable = host;

        if (_mapNextButton != null)
            _mapNextButton.interactable = host && _catalog != null && _catalog.Count > 1;
    }

    /// <summary>True when this machine is running FishNet as host.</summary>
    private static bool IsHost()
    {
        NetworkManager nm = FindAnyObjectByType<NetworkManager>();
        return nm != null && nm.IsHostStarted;
    }

    /// <summary>Host loads the selected gameplay map via the session controller.</summary>
    private void OnStartMatchClicked()
    {
        if (Time.unscaledTime < _ignoreStartMatchClicksUntil)
            return;
        _session?.StartMatchFromLobby();
    }

    /// <summary>Disconnects networking and returns to the main menu scene.</summary>
    private void OnLeaveClicked()
    {
        _session?.DisconnectAndReturnToMainMenu();
    }
}
