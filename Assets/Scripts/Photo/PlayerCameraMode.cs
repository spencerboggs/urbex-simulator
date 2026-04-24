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

    private Camera _gameplayCamera;
    private PlayerHUDController _hudController;
    private NetworkObject _networkObject;
    private CameraViewfinderUI _viewfinder;
    private GameObject _viewfinderInstance;
    private bool _cameraEquipped;
    private float _nextCaptureAllowedUnscaledTime;

    private void Awake()
    {
        _gameplayCamera = GetComponentInChildren<Camera>(true);
        _hudController = GetComponent<PlayerHUDController>();
        TryGetComponent(out _networkObject);
    }

    private void Start()
    {
        if (_viewfinderPrefab != null)
        {
            _viewfinderInstance = Instantiate(_viewfinderPrefab, transform);
            _viewfinder = _viewfinderInstance.GetComponent<CameraViewfinderUI>();
            _viewfinderInstance.SetActive(false);
        }

        Invoke(nameof(RefreshHudHint), 0.05f);
    }

    private void OnEnable()
    {
        if (_hudController != null && _hudController.isActiveAndEnabled)
            Invoke(nameof(RefreshHudHint), 0.05f);
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

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.cKey.wasPressedThisFrame)
            ToggleCamera();

        if (!_cameraEquipped || _viewfinder == null)
            return;

        Mouse mouse = Mouse.current;
        if (mouse != null &&
            mouse.leftButton.wasPressedThisFrame &&
            Time.unscaledTime >= _nextCaptureAllowedUnscaledTime)
        {
            TakePhoto();
        }
    }

    private void ToggleCamera()
    {
        if (_viewfinder == null)
            return;

        _cameraEquipped = !_cameraEquipped;
        if (_cameraEquipped)
            EnterCamera();
        else
            ExitCamera();

        RefreshHudHint();
    }

    private void EnterCamera()
    {
        if (_viewfinderInstance == null)
            return;

        _hudController?.SetMainHudVisual(false);
        _viewfinderInstance.SetActive(true);
        UpdateViewfinderHints();
    }

    private void ExitCamera()
    {
        if (_viewfinderInstance != null)
            _viewfinderInstance.SetActive(false);
        _hudController?.SetMainHudVisual(true);
    }

    private void RefreshHudHint()
    {
        if (_hudController == null)
            return;

        string key = InputBindingDisplay.GetPrimaryKeyboardDisplay("C");
        if (!_cameraEquipped)
            _hudController.SetCameraEquipHint(true, $"Press  {key}  — camera");
        else
            _hudController.SetCameraEquipHint(false, string.Empty);
    }

    private void UpdateViewfinderHints()
    {
        if (_viewfinder == null)
            return;

        string key = InputBindingDisplay.GetPrimaryKeyboardDisplay("C");
        _viewfinder.SetControlHints("Click — take photo", key);
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
