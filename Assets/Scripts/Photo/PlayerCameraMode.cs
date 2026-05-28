using System;
using System.IO;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owner-only handheld camera: viewfinder UI, scroll zoom, and saves via <see cref="PhotoRollSession"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerCameraMode : MonoBehaviour
{
    [SerializeField]
    private GameObject _viewfinderPrefab;

    [SerializeField]
    [Min(64)]
    private int _photoWidth = 1920;

    [SerializeField]
    [Min(64)]
    private int _photoHeight = 1080;

    [SerializeField]
    [Min(0.1f)]
    private float _captureCooldownSeconds = 0.4f;

    [Header("Zoom (scroll wheel)")]
    [Tooltip("FOV at full wide (no zoom). Should match the camera's default FOV.")]
    [SerializeField]
    [Range(20f, 120f)]
    private float _maxFov = 80f;

    [Tooltip("FOV at full telephoto (most zoomed in). Lower = more zoom.")]
    [SerializeField]
    [Range(5f, 90f)]
    private float _minFov = 25f;

    [Tooltip("How much zoom each scroll-wheel notch applies, as a fraction of the W↔T range.")]
    [SerializeField]
    [Range(0.01f, 0.5f)]
    private float _zoomStepFraction = 0.1f;

    [Tooltip("Higher = snappier zoom response. Lower = smoother lerp.")]
    [SerializeField]
    [Min(1f)]
    private float _zoomLerpSpeed = 12f;

    /// <summary>Child camera used for gameplay view and capture.</summary>
    private Camera _gameplayCamera;

    /// <summary>HUD controller for hints while camera mode is active.</summary>
    private PlayerHUDController _hudController;

    /// <summary>NetworkObject when spawned; null in offline test.</summary>
    private NetworkObject _networkObject;

    /// <summary>Viewfinder overlay component on the prefab instance.</summary>
    private CameraViewfinderUI _viewfinder;

    /// <summary>Instantiated viewfinder prefab root.</summary>
    private GameObject _viewfinderInstance;

    /// <summary>True when viewfinder UI and zoom are active.</summary>
    private bool _cameraEquipped;

    /// <summary>Unscaled time after which another photo capture is allowed.</summary>
    private float _nextCaptureAllowedUnscaledTime;

    /// <summary>FOV restored when stowing the camera.</summary>
    private float _defaultFov;

    /// <summary>Target normalized zoom (0 wide, 1 tele).</summary>
    private float _zoomTargetT;

    /// <summary>Smoothed normalized zoom for FOV lerp.</summary>
    private float _zoomCurrentT;

    /// <summary>True when the viewfinder is active.</summary>
    public bool IsEquipped => _cameraEquipped;

    /// <summary>Caches camera, HUD, and network references.</summary>
    private void Awake()
    {
        _gameplayCamera = GetComponentInChildren<Camera>(true);
        _hudController = GetComponent<PlayerHUDController>();
        TryGetComponent(out _networkObject);

        if (_gameplayCamera != null)
            _defaultFov = _gameplayCamera.fieldOfView;
        else
            _defaultFov = _maxFov;
    }

    /// <summary>Instantiates the viewfinder prefab as a disabled child.</summary>
    private void Start()
    {
        if (_viewfinderPrefab != null)
        {
            _viewfinderInstance = Instantiate(_viewfinderPrefab, transform);
            _viewfinder = _viewfinderInstance.GetComponent<CameraViewfinderUI>();
            _viewfinderInstance.SetActive(false);
        }
    }

    /// <summary>Clears camera equip hint when this component enables.</summary>
    private void OnEnable()
    {
        _hudController?.SetCameraEquipHint(false, string.Empty);
    }

    /// <summary>Stows camera and exits viewfinder when disabled mid-session.</summary>
    private void OnDisable()
    {
        if (_cameraEquipped)
        {
            _cameraEquipped = false;
            ExitCamera();
        }
    }

    /// <summary>Destroys the viewfinder instance with this player.</summary>
    private void OnDestroy()
    {
        if (_viewfinderInstance != null)
            Destroy(_viewfinderInstance);
    }

    /// <summary>True for owner when networked, or always when not spawned.</summary>
    private bool IsLocalControllingPlayer()
    {
        if (_networkObject == null || !_networkObject.IsSpawned)
            return true;
        return _networkObject.IsOwner;
    }

    /// <summary>Owner input: scroll zoom, left-click capture, and zoom lerp while equipped.</summary>
    private void Update()
    {
        if (!IsLocalControllingPlayer())
            return;

        if (_hudController != null && !_hudController.isActiveAndEnabled)
            return;

        if (!_cameraEquipped || _viewfinder == null)
            return;

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f)
            {
                float step = Mathf.Sign(scrollY) * _zoomStepFraction;
                _zoomTargetT = Mathf.Clamp01(_zoomTargetT + step);
            }

            if (mouse.leftButton.wasPressedThisFrame &&
                Time.unscaledTime >= _nextCaptureAllowedUnscaledTime)
            {
                TakePhoto();
            }
        }

        ApplyZoom();
    }

    /// <summary>Lerps FOV between wide and tele and updates the viewfinder zoom bar.</summary>
    private void ApplyZoom()
    {
        if (_gameplayCamera == null)
            return;

        float lerpAmount = 1f - Mathf.Exp(-_zoomLerpSpeed * Time.unscaledDeltaTime);
        _zoomCurrentT = Mathf.Lerp(_zoomCurrentT, _zoomTargetT, lerpAmount);

        float fov = Mathf.Lerp(_maxFov, _minFov, _zoomCurrentT);
        _gameplayCamera.fieldOfView = fov;

        _viewfinder?.SetZoomLevel(_zoomCurrentT);
    }

    /// <summary>Equips or stows the handheld camera viewfinder.</summary>
    public void SetEquipped(bool equipped)
    {
        if (_viewfinder == null)
            return;

        if (_cameraEquipped == equipped)
            return;

        _cameraEquipped = equipped;
        if (_cameraEquipped)
            EnterCamera();
        else
            ExitCamera();

        _hudController?.SetCameraEquipHint(false, string.Empty);
    }

    /// <summary>Activates viewfinder, resets zoom, and refreshes control hints.</summary>
    private void EnterCamera()
    {
        if (_viewfinderInstance == null)
            return;

        if (_gameplayCamera != null)
            _defaultFov = _gameplayCamera.fieldOfView;

        _zoomCurrentT = 0f;
        _zoomTargetT = 0f;
        if (_gameplayCamera != null)
            _gameplayCamera.fieldOfView = _maxFov;
        _viewfinder?.SetZoomLevel(0f);

        _viewfinderInstance.SetActive(true);
        UpdateViewfinderHints();
    }

    /// <summary>Deactivates viewfinder and restores default FOV.</summary>
    private void ExitCamera()
    {
        if (_viewfinderInstance != null)
            _viewfinderInstance.SetActive(false);

        if (_gameplayCamera != null)
            _gameplayCamera.fieldOfView = _defaultFov;
        _zoomCurrentT = 0f;
        _zoomTargetT = 0f;
    }

    /// <summary>Sets capture and stow labels on the viewfinder overlay.</summary>
    private void UpdateViewfinderHints()
    {
        if (_viewfinder == null)
            return;

        _viewfinder.SetControlHints("Click - take photo", "C");
    }

    /// <summary>Keeps min FOV from exceeding max in the inspector.</summary>
    private void OnValidate()
    {
        if (_minFov > _maxFov)
            _minFov = _maxFov;
    }

    /// <summary>Captures PNG, writes to PhotoRollSession folder, and shows saved toast.</summary>
    private void TakePhoto()
    {
        if (_gameplayCamera == null)
            return;

        _nextCaptureAllowedUnscaledTime = Time.unscaledTime + _captureCooldownSeconds;

        _viewfinder?.PlayShutterEffect();

        // Render, save under match/player folder, append manifest line.
        byte[] png = GameplayPhotoCapture.CaptureToPng(_gameplayCamera, _photoWidth, _photoHeight);
        if (png == null || png.Length == 0)
            return;

        string matchId = PhotoRollSession.ActiveMatchId;
        string playerFolder = BuildPlayerFolderLabel();
        string dir = PhotoRollSession.GetPlayerPhotoDirectoryAbsolute(matchId, playerFolder);
        string fileName =
            $"IMG_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{UnityEngine.Random.Range(0, 65535):X4}.png";
        string absolutePath = Path.Combine(dir, fileName);

        if (!GameplayPhotoCapture.SavePngBytes(png, absolutePath))
            return;

        PhotoRollSession.AppendManifestLine(matchId, playerFolder, fileName, _photoWidth, _photoHeight);
        _viewfinder?.ShowSavedToast(absolutePath);

        Debug.Log($"[Photo] Saved gameplay capture to {absolutePath}");
    }

    /// <summary>Per-player subdirectory name under the session photo roll.</summary>
    private string BuildPlayerFolderLabel()
    {
        if (_networkObject != null && _networkObject.IsSpawned && _networkObject.IsOwner)
            return $"Client_{_networkObject.Owner.ClientId}";
        return "Local";
    }
}
