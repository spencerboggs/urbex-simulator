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
        MultiplayerUiRuntimeBuilder.EnsureSceneCamera();
        MultiplayerUiRuntimeBuilder.EnsureEventSystem();

        if (_generateDefaultUiIfEmpty && _playerCountLabel == null)
            MultiplayerUiRuntimeBuilder.BuildLobby(this);

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

    private void RefreshMapLabel()
    {
        if (_mapLabel == null || _session == null)
            return;

        int idx = Mathf.Clamp(_session.SelectedMapIndex, 0, DefaultMapLabels.Length - 1);
        _session.SelectedMapIndex = idx;
        _mapLabel.text = $"Map: {DefaultMapLabels[idx]}";
    }

    private void OnMapNextClicked()
    {
        if (_session == null)
            return;

        _session.SelectedMapIndex = (_session.SelectedMapIndex + 1) % DefaultMapLabels.Length;
        RefreshMapLabel();
    }

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

    private void UpdateHostButtons()
    {
        if (_startMatchButton == null)
            return;

        NetworkManager nm = FindAnyObjectByType<NetworkManager>();
        bool host = nm != null && nm.IsHostStarted;
        _startMatchButton.interactable = host;
    }

    private void OnStartMatchClicked()
    {
        _session?.StartMatchFromLobby();
    }

    private void OnLeaveClicked()
    {
        _session?.DisconnectAndReturnToMainMenu();
    }
}
