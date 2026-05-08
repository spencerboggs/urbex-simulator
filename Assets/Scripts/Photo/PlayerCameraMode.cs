using System;
using System.IO;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

// Owner-only handheld camera
// Toggles viewfinder UI and saves captures via PhotoRollSession
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

    private Camera _gameplayCamera;
    private PlayerHUDController _hudController;
    private NetworkObject _networkObject;
    private CameraViewfinderUI _viewfinder;
    private GameObject _viewfinderInstance;
    private bool _cameraEquipped;
    private float _nextCaptureAllowedUnscaledTime;
    private float _defaultFov;
    private float _zoomTargetT;
    private float _zoomCurrentT;

    public bool IsEquipped => _cameraEquipped;

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

    private void Start()
    {
        if (_viewfinderPrefab != null)
        {
            _viewfinderInstance = Instantiate(_viewfinderPrefab, transform);
            _viewfinder = _viewfinderInstance.GetComponent<CameraViewfinderUI>();
            _viewfinderInstance.SetActive(false);
        }
    }

    private void OnEnable()
    {
        _hudController?.SetCameraEquipHint(false, string.Empty);
    }

    private void OnDisable()
    {
        if (_cameraEquipped)
        {
            _cameraEquipped = false;
            ExitCamera();
        }
    }

    private void OnDestroy()
    {
        if (_viewfinderInstance != null)
            Destroy(_viewfinderInstance);
    }

    private bool IsLocalControllingPlayer()
    {
        if (_networkObject == null || !_networkObject.IsSpawned)
            return true;
        return _networkObject.IsOwner;
    }

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
            // Scroll up = zoom in (toward T), scroll down = zoom out (toward W)
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

    private void ApplyZoom()
    {
        if (_gameplayCamera == null)
            return;

        // Lerp the displayed zoom toward the target so it feels analog
        float lerpAmount = 1f - Mathf.Exp(-_zoomLerpSpeed * Time.unscaledDeltaTime);
        _zoomCurrentT = Mathf.Lerp(_zoomCurrentT, _zoomTargetT, lerpAmount);

        float fov = Mathf.Lerp(_maxFov, _minFov, _zoomCurrentT);
        _gameplayCamera.fieldOfView = fov;

        _viewfinder?.SetZoomLevel(_zoomCurrentT);
    }

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

    private void ExitCamera()
    {
        if (_viewfinderInstance != null)
            _viewfinderInstance.SetActive(false);

        if (_gameplayCamera != null)
            _gameplayCamera.fieldOfView = _defaultFov;
        _zoomCurrentT = 0f;
        _zoomTargetT = 0f;
    }

    private void UpdateViewfinderHints()
    {
        if (_viewfinder == null)
            return;

        _viewfinder.SetControlHints("Click — take photo", "C");
    }

    private void OnValidate()
    {
        if (_minFov > _maxFov)
            _minFov = _maxFov;
    }

    private void TakePhoto()
    {
        if (_gameplayCamera == null)
            return;

        _nextCaptureAllowedUnscaledTime = Time.unscaledTime + _captureCooldownSeconds;

        _viewfinder?.PlayShutterEffect();

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

    private string BuildPlayerFolderLabel()
    {
        if (_networkObject != null && _networkObject.IsSpawned && _networkObject.IsOwner)
            return $"Client_{_networkObject.Owner.ClientId}";
        return "Local";
    }
}
