using UnityEngine;
using UnityEngine.UI;

// Main menu and Play submenu navigation. Calls NetworkSessionController only, no networking logic
public sealed class MainMenuUI : MonoBehaviour
{
    [SerializeField]
    private NetworkSessionController _session;

    [Header("Panels")]
    [SerializeField]
    private GameObject _mainPanel;

    [SerializeField]
    private GameObject _playPanel;

    [Header("Optional")]
    [SerializeField]
    private InputField _joinAddressInput;

    [SerializeField]
    private Button _playButton;

    [SerializeField]
    private Button _quitButton;

    [SerializeField]
    private Button _playMenuHostButton;

    [SerializeField]
    private Button _playMenuJoinButton;

    [SerializeField]
    private Button _playMenuBackButton;

    [SerializeField]
    [Tooltip("If no panels are assigned, a default Canvas is generated at runtime.")]
    private bool _generateDefaultUiIfEmpty = true;

    private bool _wired;

    private void Awake()
    {
        // MainMenu scene may have no Camera (copied from level); Game view shows "No cameras rendering" and UI can fail to show.
        MultiplayerUiRuntimeBuilder.EnsureSceneCamera();
        MultiplayerUiRuntimeBuilder.EnsureEventSystem();

        if (_generateDefaultUiIfEmpty && _mainPanel == null)
            MultiplayerUiRuntimeBuilder.BuildMainMenu(this);

        if (_session == null)
            _session = FindAnyObjectByType<NetworkSessionController>();

        if (_joinAddressInput != null && _session != null && !string.IsNullOrEmpty(_session.DefaultJoinAddress))
            _joinAddressInput.text = _session.DefaultJoinAddress;

        WireButtonsOnce();
        ShowMain();
    }

    public void ApplyRuntimeReferences(
        GameObject mainPanel,
        GameObject playPanel,
        InputField joinAddressInput,
        Button playButton,
        Button quitButton,
        Button hostButton,
        Button joinButton,
        Button backButton)
    {
        _mainPanel = mainPanel;
        _playPanel = playPanel;
        _joinAddressInput = joinAddressInput;
        _playButton = playButton;
        _quitButton = quitButton;
        _playMenuHostButton = hostButton;
        _playMenuJoinButton = joinButton;
        _playMenuBackButton = backButton;
    }

    private void WireButtonsOnce()
    {
        if (_wired)
            return;
        _wired = true;

        if (_playButton != null)
            _playButton.onClick.AddListener(ShowPlay);
        if (_quitButton != null)
            _quitButton.onClick.AddListener(QuitApplication);

        if (_playMenuBackButton != null)
            _playMenuBackButton.onClick.AddListener(ShowMain);

        if (_playMenuHostButton != null)
            _playMenuHostButton.onClick.AddListener(OnHostClicked);

        if (_playMenuJoinButton != null)
            _playMenuJoinButton.onClick.AddListener(OnJoinClicked);
    }

    public void ShowMain()
    {
        if (_mainPanel != null)
            _mainPanel.SetActive(true);
        if (_playPanel != null)
            _playPanel.SetActive(false);
    }

    public void ShowPlay()
    {
        if (_mainPanel != null)
            _mainPanel.SetActive(false);
        if (_playPanel != null)
            _playPanel.SetActive(true);
    }

    public void OnHostClicked()
    {
        if (_session == null)
            return;
        _session.StartHost();
    }

    public void OnJoinClicked()
    {
        if (_session == null)
            return;

        string address = _joinAddressInput != null ? _joinAddressInput.text : null;
        _session.StartClient(address);
    }

    private static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif !UNITY_SERVER
        Application.Quit();
#endif
    }
}
