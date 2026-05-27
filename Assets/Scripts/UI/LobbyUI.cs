using FishNet.Managing;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


// Lobby staging UI: player count (up to 8), map selection, host-only start match, leave session
//
// Map data comes from MapCatalog (auto-synced from Assets/Scenes/Gameplay/ by
// Editor/MapCatalogAutoSync.cs). The host's "Next Map" click is routed through
// NetworkSessionController.RequestMapChange so the server can validate and
// replicate the change to every connected client. Non-host clients have the
// button disabled and only observe the host's selection via broadcast
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

    private MapCatalog _catalog;
    private bool _wired;
    private float _ignoreStartMatchClicksUntil;
    private bool _subscribedToSession;

    private void Awake()
    {
        // Ensure essential UI components exist in the scene for both authored and generated UI
        MultiplayerUiRuntimeBuilder.EnsureSceneCamera();
        MultiplayerUiRuntimeBuilder.EnsureEventSystem();

        // If the player count label is not assigned, we assume the entire UI needs to be generated
        if (_generateDefaultUiIfEmpty && _playerCountLabel == null)
            MultiplayerUiRuntimeBuilder.BuildLobby(this);

        // If a session reference isn't assigned, attempt to find one in the scene
        if (_session == null)
            _session = FindAnyObjectByType<NetworkSessionController>();

        _catalog = MapCatalog.Load();
        if (_catalog == null || _catalog.Count == 0)
        {
            Debug.LogWarning("[LobbyUI] MapCatalog is missing or empty. Add scenes to Assets/Scenes/Gameplay/ and run Tools/Urbex/Refresh Map Catalog.");
        }

        WireListenersOnce();
    }

    private void OnEnable()
    {
        SubscribeToSession();
        RefreshMapLabel();
        // On scene load, InputSystemUIInputModule can dispatch an initial Submit
        // If a button is selected, that Submit can invoke onClick. Debounce and clear selection
        _ignoreStartMatchClicksUntil = Time.unscaledTime + 0.25f;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void OnDisable()
    {
        UnsubscribeFromSession();
    }

    private void Update()
    {
        UpdatePlayerCount();
        UpdateHostButtons();
    }

    // Used by MultiplayerUiRuntimeBuilder for default lobby layout
    public void ApplyRuntimeReferences(Text playerCount, Text mapLabel, Button mapNext, Button startMatch, Button leave)
    {
        _playerCountLabel = playerCount;
        _mapLabel = mapLabel;
        _mapNextButton = mapNext;
        _startMatchButton = startMatch;
        _leaveButton = leave;
    }

    private void WireListenersOnce()
    {
        // Prevent double-wiring if ApplyRuntimeReferences is called multiple times
        if (_wired)
            return;
        _wired = true;

        // Wire up button listeners for map cycling, starting the match, and leaving the lobby
        if (_startMatchButton != null)
            _startMatchButton.onClick.AddListener(OnStartMatchClicked);

        if (_leaveButton != null)
            _leaveButton.onClick.AddListener(OnLeaveClicked);

        if (_mapNextButton != null)
            _mapNextButton.onClick.AddListener(OnMapNextClicked);
    }

    private void SubscribeToSession()
    {
        if (_session == null)
            _session = FindAnyObjectByType<NetworkSessionController>();
        if (_session == null || _subscribedToSession)
            return;

        _session.OnSelectedMapChanged += OnSelectedMapChanged;
        _subscribedToSession = true;
    }

    private void UnsubscribeFromSession()
    {
        if (_session == null || !_subscribedToSession)
            return;

        _session.OnSelectedMapChanged -= OnSelectedMapChanged;
        _subscribedToSession = false;
    }

    private void OnSelectedMapChanged(string sceneName)
    {
        RefreshMapLabel();
    }

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

        // Fall back to the first catalog entry for display purposes only - this
        // doesn't change the session's selection, just keeps the UI from
        // showing a blank value before the host has picked anything
        if (string.IsNullOrEmpty(sceneName) && _catalog.TryGetByIndex(0, out MapCatalog.MapEntry first))
            sceneName = first.sceneName;

        if (_catalog.TryGetBySceneName(sceneName, out MapCatalog.MapEntry entry, out _))
            _mapLabel.text = $"Map: {entry.displayName}";
        else
            _mapLabel.text = $"Map: {sceneName}";
    }

    private void OnMapNextClicked()
    {
        if (_session == null || _catalog == null || _catalog.Count == 0)
            return;

        // Non-host clients shouldn't get here (UI gates it), but guard anyway.
        if (!IsHost())
            return;

        string currentScene = _session.SelectedMapSceneName;
        int currentIndex = -1;
        if (!string.IsNullOrEmpty(currentScene))
            _catalog.TryGetBySceneName(currentScene, out _, out currentIndex);

        int nextIndex = (currentIndex + 1) % _catalog.Count;
        if (_catalog.TryGetByIndex(nextIndex, out MapCatalog.MapEntry nextEntry))
            _session.RequestMapChange(nextEntry.sceneName);
    }

    private void UpdatePlayerCount()
    {
        if (_playerCountLabel == null)
            return;

        // If the NetworkManager isn't active, display a placeholder
        // Otherwise, show the current number of connected clients out of a maximum of 8
        NetworkManager nm = FindAnyObjectByType<NetworkManager>();
        if (nm == null || !nm.IsServerStarted)
        {
            _playerCountLabel.text = "Players: --";
            return;
        }

        int count = nm.ServerManager.Clients.Count;
        _playerCountLabel.text = $"Players: {count} / 8";
    }

    private void UpdateHostButtons()
    {
        bool host = IsHost();

        // Only the host can start the match; the lobby is host-driven
        if (_startMatchButton != null)
            _startMatchButton.interactable = host;

        // Only the host can cycle maps, and only when there's more than one
        // choice. Non-host clients see the label update via the broadcast but
        // cannot change the selection themselves
        if (_mapNextButton != null)
            _mapNextButton.interactable = host && _catalog != null && _catalog.Count > 1;
    }

    private static bool IsHost()
    {
        NetworkManager nm = FindAnyObjectByType<NetworkManager>();
        return nm != null && nm.IsHostStarted;
    }

    private void OnStartMatchClicked()
    {
        // In this simple implementation, the host can start the match once ready, which triggers a global scene load for all clients
        if (Time.unscaledTime < _ignoreStartMatchClicksUntil)
            return;
        _session?.StartMatchFromLobby();
    }

    private void OnLeaveClicked()
    {
        // Disconnect from the session and return to the main menu scene
        _session?.DisconnectAndReturnToMainMenu();
    }
}
