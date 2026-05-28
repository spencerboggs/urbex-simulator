using UnityEngine;
using UnityEngine.UI;

/// <summary>Main menu and play submenu; delegates networking to <see cref="NetworkSessionController"/>.</summary>
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

    /// <summary>True after menu button listeners are registered once.</summary>
    private bool _wired;

    /// <summary>Builds default UI, resolves session, and shows the main panel.</summary>
    private void Awake()
    {
        MultiplayerUiRuntimeBuilder.EnsureSceneCamera();
        MultiplayerUiRuntimeBuilder.EnsureEventSystem();

        if (_generateDefaultUiIfEmpty && _mainPanel == null)
            MultiplayerUiRuntimeBuilder.BuildMainMenu(this);

        if (_session == null)
            _session = FindAnyObjectByType<NetworkSessionController>();

        if (_joinAddressInput != null && _session != null && !string.IsNullOrEmpty(_session.DefaultJoinTarget))
            _joinAddressInput.text = _session.DefaultJoinTarget;

        WireButtonsOnce();
        ShowMain();
    }

    /// <summary>Wires runtime-generated UI references from <see cref="MultiplayerUiRuntimeBuilder"/>.</summary>
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

    /// <summary>Registers onClick handlers for main and play submenu buttons.</summary>
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

    /// <summary>Shows the main menu panel.</summary>
    public void ShowMain()
    {
        if (_mainPanel != null)
            _mainPanel.SetActive(true);
        if (_playPanel != null)
            _playPanel.SetActive(false);
    }

    /// <summary>Shows the play submenu panel.</summary>
    public void ShowPlay()
    {
        if (_mainPanel != null)
            _mainPanel.SetActive(false);
        if (_playPanel != null)
            _playPanel.SetActive(true);
    }

    /// <summary>Starts hosting and transitions to the lobby when ready.</summary>
    public void OnHostClicked()
    {
        if (_session == null)
            return;
        _session.StartHost();
    }

    /// <summary>Joins using the join address field.</summary>
    public void OnJoinClicked()
    {
        if (_session == null)
            return;

        string joinFieldText = _joinAddressInput != null ? _joinAddressInput.text : null;
        _session.StartClient(joinFieldText);
    }

    /// <summary>Stops play mode in editor or quits the standalone build.</summary>
    private static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif !UNITY_SERVER
        Application.Quit();
#endif
    }
}
