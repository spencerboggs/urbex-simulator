using FishNet.Managing;
using UnityEngine;
using UnityEngine.UI;


// Lobby staging UI: player count (up to 8), map selection, host-only start match, leave session
public sealed class LobbyUI : MonoBehaviour
{
    private static readonly string[] DefaultMapLabels = { "Level (default)", "Future map B" };

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

    private bool _wired;

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

        WireListenersOnce();
    }

    private void OnEnable()
    {
        RefreshMapLabel();
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

    private void RefreshMapLabel()
    {
        if (_mapLabel == null || _session == null)
            return;

        // Clamp the selected map index to valid bounds and update the label text accordingly
        int idx = Mathf.Clamp(_session.SelectedMapIndex, 0, DefaultMapLabels.Length - 1);
        _session.SelectedMapIndex = idx;
        _mapLabel.text = $"Map: {DefaultMapLabels[idx]}";
    }

    private void OnMapNextClicked()
    {
        if (_session == null)
            return;

        // Cycle through available maps by incrementing the selected index and wrapping around using modulo
        _session.SelectedMapIndex = (_session.SelectedMapIndex + 1) % DefaultMapLabels.Length;
        RefreshMapLabel();
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
        if (_startMatchButton == null)
            return;

        // Only enable the Start Match button for the host, since progression is host-driven in this implementation
        NetworkManager nm = FindAnyObjectByType<NetworkManager>();
        bool host = nm != null && nm.IsHostStarted;
        _startMatchButton.interactable = host;
    }

    private void OnStartMatchClicked()
    {   
        // In this simple implementation, the host can start the match once ready, which triggers a global scene load for all clients
        _session?.StartMatchFromLobby();
    }

    private void OnLeaveClicked()
    {
        // Disconnect from the session and return to the main menu scene
        _session?.DisconnectAndReturnToMainMenu();
    }
}
